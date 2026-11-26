using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace MediaFileClassifier.Desktop.Platform;

/// <summary>
/// What the Windows shell did with a file <see cref="ShellRecycler"/> gave it.
/// </summary>
internal enum ShellRecycleOutcome
{
    /// <summary>The file is in the Recycle Bin.</summary>
    Recycled,

    /// <summary>The shell would have deleted the file permanently, so it was refused and the file is still there.</summary>
    Refused,

    /// <summary>The shell failed, e.g. the file is open in another program.</summary>
    Failed,
}

/// <summary>
/// Sends one file to the Recycle Bin through the shell's <c>IFileOperation</c> (E12-S04-T08, D024), with no UI. The shell
/// decides per file whether it can recycle it: on a location without a Recycle Bin (a network share, most removable
/// drives) or for a file too big for the bin, it would delete the file permanently instead. The progress sink sees that
/// decision before the delete (<c>PreDeleteItem</c> without <c>TSF_DELETE_RECYCLE_IF_POSSIBLE</c>) and refuses the item,
/// so nothing is ever deleted permanently. Must run on an STA thread, as <c>IFileOperation</c> requires.
/// </summary>
internal static partial class ShellRecycler
{
    // FOF_SILENT | FOF_NOCONFIRMATION | FOF_NOCONFIRMMKDIR | FOF_NOERRORUI: no window, no question.
    private const uint FofNoUi = 0x0614;
    private const uint FofAllowUndo = 0x0040;
    private const uint FofxRecycleOnDelete = 0x0008_0000;
    private const uint FofxEarlyFailure = 0x0010_0000;
    private const uint ClsctxInprocServer = 0x1;

    private static readonly Guid FileOperationClassId = new("3ad05575-8857-4850-9277-11b85bdb8e09");
    private static readonly Guid FileOperationInterfaceId = new("947aab5f-0a5c-4c13-b4d6-4bf7836fc9f8");
    private static readonly Guid ShellItemInterfaceId = new("43826d1e-e718-42ee-bc55-a1e261c37bfe");

    /// <summary>
    /// Sends a file to the Recycle Bin, or refuses to if the shell can't recycle it.
    /// </summary>
    /// <param name="path">The file's full path.</param>
    /// <returns>What happened, and the error for <see cref="ShellRecycleOutcome.Failed"/>.</returns>
    public static (ShellRecycleOutcome Outcome, string? Error) Recycle(string path)
    {
        if (Thread.CurrentThread.GetApartmentState() != ApartmentState.STA)
        {
            throw new InvalidOperationException("IFileOperation needs an STA thread.");
        }

        var hr = CoCreateInstance(FileOperationClassId, 0, ClsctxInprocServer, FileOperationInterfaceId, out var operation);
        if (hr < 0)
        {
            return (ShellRecycleOutcome.Failed, Describe("Creating the shell file operation", hr));
        }

        nint item = 0;
        try
        {
            hr = SHCreateItemFromParsingName(path, 0, ShellItemInterfaceId, out item);
            if (hr < 0)
            {
                return (ShellRecycleOutcome.Failed, Describe("Opening the file in the shell", hr));
            }

            var sink = new RecycleOnlySink();
            hr = operation.SetOperationFlags(FofNoUi | FofAllowUndo | FofxRecycleOnDelete | FofxEarlyFailure);
            if (hr >= 0)
            {
                hr = operation.DeleteItem(item, sink);
            }

            if (hr >= 0)
            {
                hr = operation.PerformOperations();
            }

            // The veto comes first: PerformOperations reports it as a cancelled operation.
            if (sink.Refused)
            {
                return (ShellRecycleOutcome.Refused, null);
            }

            if (sink.DeleteResult is { } deleteResult && deleteResult < 0)
            {
                return (ShellRecycleOutcome.Failed, Describe("The shell", deleteResult));
            }

            if (hr < 0)
            {
                return (ShellRecycleOutcome.Failed, Describe("The shell", hr));
            }

            if (sink.DeleteResult is null || operation.GetAnyOperationsAborted(out var aborted) < 0 || aborted != 0)
            {
                return (ShellRecycleOutcome.Failed, "The shell didn't delete the file.");
            }

            // The sink only lets a recycling delete through, so the shell must have put the file in the bin.
            return sink.Recycled
                ? (ShellRecycleOutcome.Recycled, null)
                : (ShellRecycleOutcome.Failed, "The shell reported no item in the Recycle Bin for the file.");
        }
        finally
        {
            if (item != 0)
            {
                Marshal.Release(item);
            }

            if ((object)operation is ComObject comObject)
            {
                comObject.FinalRelease();
            }
        }
    }

    /// <summary>
    /// Tells whether the shell will recycle an item, from the flags it passes to <c>PreDeleteItem</c>.
    /// </summary>
    /// <param name="transferFlags">The <c>TRANSFER_SOURCE_FLAGS</c>.</param>
    /// <returns><see langword="true"/> if the item goes to the Recycle Bin, <see langword="false"/> if deleting it would be
    /// permanent.</returns>
    public static bool WillRecycle(uint transferFlags) => (transferFlags & RecycleOnlySink.DeleteRecycleIfPossible) != 0;

    private static string Describe(string what, int hr) =>
        $"{what} failed: {Marshal.GetExceptionForHR(hr)?.Message ?? string.Empty} (0x{hr:X8}).";

    [LibraryImport("ole32.dll")]
    private static partial int CoCreateInstance(
        in Guid classId, nint outer, uint context, in Guid interfaceId, out IFileOperation operation);

    [LibraryImport("shell32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int SHCreateItemFromParsingName(string path, nint bindContext, in Guid interfaceId, out nint item);

    /// <summary>
    /// Lets a delete through only when the shell will recycle the item, and records what the shell did.
    /// </summary>
    [GeneratedComClass]
    internal sealed partial class RecycleOnlySink : IFileOperationProgressSink
    {
        /// <summary><c>TSF_DELETE_RECYCLE_IF_POSSIBLE</c>: the shell will recycle the item.</summary>
        internal const uint DeleteRecycleIfPossible = 0x80;

        // HRESULT_FROM_WIN32(ERROR_CANCELLED).
        private const int Cancelled = unchecked((int)0x800704C7);

        public bool Refused { get; private set; }

        public int? DeleteResult { get; private set; }

        public bool Recycled { get; private set; }

        public int PreDeleteItem(uint flags, nint item)
        {
            if (WillRecycle(flags))
            {
                return 0;
            }

            Refused = true;
            return Cancelled;
        }

        public int PostDeleteItem(uint flags, nint item, int result, nint newlyCreated)
        {
            DeleteResult = result;
            Recycled = newlyCreated != 0;
            return 0;
        }

        public int StartOperations() => 0;

        public int FinishOperations(int result) => 0;

        public int PreRenameItem(uint flags, nint item, nint newName) => 0;

        public int PostRenameItem(uint flags, nint item, nint newName, int result, nint newlyCreated) => 0;

        public int PreMoveItem(uint flags, nint item, nint destinationFolder, nint newName) => 0;

        public int PostMoveItem(uint flags, nint item, nint destinationFolder, nint newName, int result, nint newlyCreated) => 0;

        public int PreCopyItem(uint flags, nint item, nint destinationFolder, nint newName) => 0;

        public int PostCopyItem(uint flags, nint item, nint destinationFolder, nint newName, int result, nint newlyCreated) => 0;

        public int PreNewItem(uint flags, nint destinationFolder, nint newName) => 0;

        public int PostNewItem(uint flags, nint destinationFolder, nint newName, nint templateName, uint attributes, int result, nint newItem) => 0;

        public int UpdateProgress(uint workTotal, uint workSoFar) => 0;

        public int ResetTimer() => 0;

        public int PauseTimer() => 0;

        public int ResumeTimer() => 0;
    }
}

/// <summary>
/// The shell's <c>IFileOperation</c>, in vtable order. Only the members used are typed; the rest take raw pointers.
/// </summary>
[GeneratedComInterface]
[Guid("947aab5f-0a5c-4c13-b4d6-4bf7836fc9f8")]
internal partial interface IFileOperation
{
    [PreserveSig]
    int Advise(IFileOperationProgressSink sink, out uint cookie);

    [PreserveSig]
    int Unadvise(uint cookie);

    [PreserveSig]
    int SetOperationFlags(uint flags);

    [PreserveSig]
    int SetProgressMessage(nint message);

    [PreserveSig]
    int SetProgressDialog(nint dialog);

    [PreserveSig]
    int SetProperties(nint properties);

    [PreserveSig]
    int SetOwnerWindow(nint window);

    [PreserveSig]
    int ApplyPropertiesToItem(nint item);

    [PreserveSig]
    int ApplyPropertiesToItems(nint items);

    [PreserveSig]
    int RenameItem(nint item, nint newName, nint sink);

    [PreserveSig]
    int RenameItems(nint items, nint newName);

    [PreserveSig]
    int MoveItem(nint item, nint destinationFolder, nint newName, nint sink);

    [PreserveSig]
    int MoveItems(nint items, nint destinationFolder);

    [PreserveSig]
    int CopyItem(nint item, nint destinationFolder, nint copyName, nint sink);

    [PreserveSig]
    int CopyItems(nint items, nint destinationFolder);

    [PreserveSig]
    int DeleteItem(nint item, IFileOperationProgressSink sink);

    [PreserveSig]
    int DeleteItems(nint items);

    [PreserveSig]
    int NewItem(nint destinationFolder, uint attributes, nint name, nint templateName, nint sink);

    [PreserveSig]
    int PerformOperations();

    [PreserveSig]
    int GetAnyOperationsAborted(out int aborted);
}

/// <summary>
/// The shell's <c>IFileOperationProgressSink</c>, in vtable order.
/// </summary>
[GeneratedComInterface]
[Guid("04b0f1a7-9490-44bc-96e1-4296a31252e2")]
internal partial interface IFileOperationProgressSink
{
    [PreserveSig]
    int StartOperations();

    [PreserveSig]
    int FinishOperations(int result);

    [PreserveSig]
    int PreRenameItem(uint flags, nint item, nint newName);

    [PreserveSig]
    int PostRenameItem(uint flags, nint item, nint newName, int result, nint newlyCreated);

    [PreserveSig]
    int PreMoveItem(uint flags, nint item, nint destinationFolder, nint newName);

    [PreserveSig]
    int PostMoveItem(uint flags, nint item, nint destinationFolder, nint newName, int result, nint newlyCreated);

    [PreserveSig]
    int PreCopyItem(uint flags, nint item, nint destinationFolder, nint newName);

    [PreserveSig]
    int PostCopyItem(uint flags, nint item, nint destinationFolder, nint newName, int result, nint newlyCreated);

    [PreserveSig]
    int PreDeleteItem(uint flags, nint item);

    [PreserveSig]
    int PostDeleteItem(uint flags, nint item, int result, nint newlyCreated);

    [PreserveSig]
    int PreNewItem(uint flags, nint destinationFolder, nint newName);

    [PreserveSig]
    int PostNewItem(uint flags, nint destinationFolder, nint newName, nint templateName, uint attributes, int result, nint newItem);

    [PreserveSig]
    int UpdateProgress(uint workTotal, uint workSoFar);

    [PreserveSig]
    int ResetTimer();

    [PreserveSig]
    int PauseTimer();

    [PreserveSig]
    int ResumeTimer();
}
