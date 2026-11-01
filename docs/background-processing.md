# Background processing

The job system (E05-S01) runs every long operation: import stages, AI analysis, full hashing and maintenance. The API is in `Infrastructure.Abstractions/Jobs`, the implementation in `Infrastructure.Jobs`, and the tables in `Infrastructure.DataAccess`.

## Model

- **Job** (`ProcessingJob`): a type, a description, a target (library, folder or item set), a priority and a state. States follow `ProcessingJobStateTransitions`: `Queued → Running → Completed`, or `Failed`, `Cancelled`, and `WaitingForModel` (E06-S12). `Paused` is reserved for E05-S05.
- **Work item** (`WorkItem`): one stage for one media item, or for the job's whole target, e.g. `thumbnail` for one item. It has a resource pool, the job's priority, a state (`WorkItemStateTransitions`), an attempt count, the last error and an optional JSON payload.
- **Storage:** both are rows in the library database (`processing_job`, `work_item`, schema version 4), written through the serialized writer (D072). So unfinished work survives a crash or a restart.
- **Job outcome:** when a job's last work item finishes, the job is `Completed` if any item completed, even when others failed (they keep their error for a retry, E05-S04), and `Failed` only if they all failed.

## Using it from another Epic

1. **Write a stage handler** for each stage, and register it in your module:

   ```csharp
   internal sealed class ThumbnailStage(IThumbnailRenderer renderer) : IStageHandler
   {
       public string Stage => "thumbnail";

       public ResourcePool Pool => ResourcePool.Cpu;

       public async Task HandleAsync(WorkItemContext context, CancellationToken cancellationToken)
       {
           var item = await context.Library.Items.GetAsync(context.WorkItem.ItemId!.Value, cancellationToken);
           await renderer.RenderAsync(item!, context.Library.Layout, cancellationToken);
           await context.Scheduler.EnqueueWorkAsync(context.WorkItem.JobId, [new WorkItemRequest("register", item!.Id)], cancellationToken);
       }
   }

   services.TryAddEnumerable(ServiceDescriptor.Singleton<IStageHandler, ThumbnailStage>());
   ```

   - **Stage keys** are stored with each work item, so keep them stable.
   - **Commit once, at the end.** A cancelled or failed work item must leave no partial result.
   - **Pass the cancellation token** to everything slow: file reads, ONNX, HTTP, FFmpeg. It's cancelled when the job is cancelled, and 2 seconds after the library starts closing.
   - **Throwing** fails the work item with the exception's message. Other work items carry on.
   - **Next stages** are enqueued with `EnqueueWorkAsync` on the same job, so they share its priority, progress and cancellation.

2. **Enqueue a job** through the open library's scheduler:

   ```csharp
   var scheduler = jobSystem.Current ?? throw new InvalidOperationException("No library is open.");
   var jobId = await scheduler.EnqueueJobAsync(
       new JobRequest("import", "Import 2024 photos", JobPriority.Import, [.. files.Select(file => new WorkItemRequest("fingerprint", file.Id))])
       {
           FolderId = folder.Id,
       },
       cancellationToken);
   ```

3. **Cancel** with `CancelJobAsync(jobId)`: queued work items are cancelled, running ones get their token cancelled, and finished ones keep their results.

4. **Boost** what's on screen with `Boost(itemIds)`. The media browser (E14) calls it with the visible items whenever the view scrolls; each call replaces the previous set.

## Scheduling

- **Pools:** I/O, CPU and AI, each with its own concurrency limit. The defaults are 4 for I/O, `IHardwareInfo.DefaultParallelism` for the CPU, and 1 for AI. `AppSettings.Processing` overrides them, and `SetPoolLimit` changes them at runtime (E17).
- **Order:** user-initiated actions first, then boosted items, then by `JobPriority` (import, AI analysis, full hashing, maintenance), then oldest first.
- **Dispatcher:** one loop per open library, on the thread pool, never on the UI thread. It reads ahead up to 64 queued work items per pool from the database, in run order, and reads again when new work arrives. Handlers run on thread-pool threads.

## Library lifetime

Whoever opens and closes libraries (E02) calls the `IJobSystem`:

- **After opening:** `StartAsync(library)`. Work items left `Running` by a crash or by quitting go back to `Queued`, and their jobs too. Processing then resumes at once, or waits for `Resume()` when `AppSettings.Processing.ResumeOnOpen` is `Ask`.
- **Before closing:** `StopAsync()`. No new work starts, running work gets 2 seconds to finish, and the rest is cancelled and put back in the queue. Nothing is written to the library after it returns.
- **Quitting (D027):** `QuitGuard` handles the main window's closing. If work is pending it asks first ("340 items are still processing…", with "Don't ask again"), then stops the jobs and closes. **Library › Exit** closes the window the same way.

## Not here yet

- Progress, the processing panel, retries and pausing are E05-S02 to S05.
- The import pipeline (E03-S07) and the maintenance commands (E02) are the first users of this API.
