using System.ComponentModel;
using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Text.Json;
using FFMpegCore.Exceptions;
using MediaFileClassifier.Domain.Jobs;
using MediaFileClassifier.Infrastructure.Abstractions.AI;
using MediaFileClassifier.Infrastructure.Abstractions.Errors;
using MediaFileClassifier.Infrastructure.AI;
using MediaFileClassifier.Infrastructure.DataAccess.Database;
using MediaFileClassifier.Infrastructure.Jobs.Errors;
using MediaFileClassifier.Infrastructure.Media;
using MediaFileClassifier.UseCases.Import;
using MetadataExtractor;
using Microsoft.Data.Sqlite;
using Microsoft.ML.OnnxRuntime;

namespace MediaFileClassifier.Infrastructure.Tests.Jobs;

/// <summary>
/// Tests for <see cref="ErrorClassifier"/> with every module's classifier registered (E05-S04-T01).
/// </summary>
public sealed class ErrorClassifierTests
{
    private const string Provider = "lm-studio";

    private readonly ErrorClassifier classifier = new(
    [
        new AiExceptionClassifier(),
        new MediaExceptionClassifier(),
        new DatabaseExceptionClassifier(),
        new ImportExceptionClassifier(),
    ]);

    public static TheoryData<Exception, ErrorCategory> Exceptions => new()
    {
        // File I/O.
        { new IOException("locked", unchecked((int)0x80070020)), ErrorCategory.Transient },
        { new IOException("lock violation", unchecked((int)0x80070021)), ErrorCategory.Transient },
        { new IOException("disk full", unchecked((int)0x80070070)), ErrorCategory.Configuration },
        { new IOException("share dropped"), ErrorCategory.Transient },
        { new FileNotFoundException("gone"), ErrorCategory.Permanent },
        { new DirectoryNotFoundException("drive offline"), ErrorCategory.Transient },
        { new DriveNotFoundException("drive offline"), ErrorCategory.Transient },
        { new EndOfStreamException("truncated"), ErrorCategory.Permanent },
        { new PathTooLongException("long"), ErrorCategory.Permanent },
        { new UnauthorizedAccessException("denied"), ErrorCategory.Permanent },

        // HTTP and timeouts.
        { new HttpRequestException("refused"), ErrorCategory.Transient },
        { new HttpRequestException("rate limit", null, HttpStatusCode.TooManyRequests), ErrorCategory.Transient },
        { new HttpRequestException("server", null, HttpStatusCode.BadGateway), ErrorCategory.Transient },
        { new HttpRequestException("bad request", null, HttpStatusCode.BadRequest), ErrorCategory.Permanent },
        { new SocketException(10061), ErrorCategory.Transient },
        { new TimeoutException(), ErrorCategory.Transient },
        { new TaskCanceledException(), ErrorCategory.Transient },

        // Decoders and parsers.
        { new InvalidDataException("corrupt"), ErrorCategory.Permanent },
        { new FormatException("corrupt"), ErrorCategory.Permanent },
        { new NotSupportedException("codec"), ErrorCategory.Permanent },
        { new JsonException("bad json"), ErrorCategory.Permanent },
        { new ImageProcessingException("bad exif"), ErrorCategory.Permanent },
        { new DllNotFoundException("native"), ErrorCategory.Configuration },

        // FFmpeg.
        { new FFMpegException(FFMpegExceptionType.Process, "can't decode"), ErrorCategory.Permanent },
        { new FFProbeException("can't probe"), ErrorCategory.Permanent },
        { new Win32Exception(2), ErrorCategory.Configuration },

        // SQLite.
        { new SqliteException("busy", 5), ErrorCategory.Transient },
        { new SqliteException("locked", 6), ErrorCategory.Transient },

        // Import.
        { new MediaFileException("damaged"), ErrorCategory.Permanent },
        { new MediaFileException("locked") { IsPermanent = false }, ErrorCategory.Transient },

        // Bugs.
        { new NullReferenceException(), ErrorCategory.Permanent },
        { new InvalidOperationException("bug"), ErrorCategory.Permanent },
    };

    public static TheoryData<AiProviderException, ErrorCategory, bool> ProviderFailures => new()
    {
        { Ai(AiFailureKind.Unreachable), ErrorCategory.Transient, true },
        { Ai(AiFailureKind.Timeout), ErrorCategory.Transient, true },
        { Ai(AiFailureKind.ProviderError), ErrorCategory.Transient, true },
        { Ai(AiFailureKind.ProviderError, new HttpRequestException("server", null, HttpStatusCode.InternalServerError)), ErrorCategory.Transient, true },
        { Ai(AiFailureKind.ProviderError, new HttpRequestException("rate limit", null, HttpStatusCode.TooManyRequests)), ErrorCategory.Transient, true },
        { Ai(AiFailureKind.ProviderError, new JsonException("not OpenAI")), ErrorCategory.Transient, true },
        { Ai(AiFailureKind.ProviderError, new HttpRequestException("rejected", null, HttpStatusCode.BadRequest)), ErrorCategory.Permanent, false },
        { Ai(AiFailureKind.ProviderError, Uninitialized<OnnxRuntimeException>()), ErrorCategory.Permanent, false },
        { Ai(AiFailureKind.InvalidInput), ErrorCategory.Permanent, false },
        { Ai(AiFailureKind.InvalidOutput), ErrorCategory.Permanent, false },
        { Ai(AiFailureKind.RemoteHostNotConfirmed), ErrorCategory.Configuration, true },
        { Ai(AiFailureKind.ModelMissing), ErrorCategory.Configuration, false },
    };

    [Theory]
    [MemberData(nameof(Exceptions))]
    public void Classify_KnownException_HasItsCategory(Exception exception, ErrorCategory expected)
    {
        var classified = classifier.Classify(exception);

        Assert.Equal(expected, classified.Category);
        Assert.Same(exception, classified.Exception);
        Assert.False(classified.IsProviderFailure);
    }

    [Theory]
    [MemberData(nameof(ProviderFailures))]
    public void Classify_AiProviderException_ByKindAndCause(AiProviderException exception, ErrorCategory expected, bool providerFailure)
    {
        var classified = classifier.Classify(exception);

        Assert.Equal(expected, classified.Category);
        Assert.Equal(providerFailure, classified.IsProviderFailure);
        Assert.Equal(Provider, classified.ProviderId);
        Assert.Equal(exception.Message, classified.Message);
    }

    [Fact]
    public void Classify_ModelMissing_IsMarkedAsWaitingForTheModel()
    {
        var classified = classifier.Classify(Ai(AiFailureKind.ModelMissing));

        Assert.True(classified.IsModelMissing);
        Assert.False(classified.IsProviderFailure);
    }

    [Fact]
    public void Classify_FolderOffline_IsMarkedAsWaitingForTheFolder()
    {
        var classified = classifier.Classify(FolderOfflineException.For(@"E:\Photos", new DirectoryNotFoundException("gone")));

        Assert.True(classified.IsFolderOffline);
        Assert.False(classified.IsModelMissing);
        Assert.False(classified.IsProviderFailure);
        Assert.Equal(@"The folder E:\Photos is offline.", classified.Message);
    }

    [Fact]
    public void Classify_RawOnnxRuntimeException_IsPermanent()
    {
        Assert.Equal(ErrorCategory.Permanent, classifier.Classify(Uninitialized<OnnxRuntimeException>()).Category);
    }

    [Fact]
    public void Classify_ProviderExceptionWithoutProvider_IsNoProviderFailure()
    {
        var classified = classifier.Classify(new AiProviderException(AiFailureKind.Unreachable, null!, "down"));

        Assert.Equal(ErrorCategory.Transient, classified.Category);
        Assert.False(classified.IsProviderFailure);
    }

    [Fact]
    public void Classify_WrappedException_UsesTheInnerCategoryAndTheOuterMessage()
    {
        var exception = new InvalidOperationException("Couldn't read photo.jpg.", new IOException("in use", unchecked((int)0x80070020)));

        var classified = classifier.Classify(exception);

        Assert.Equal(ErrorCategory.Transient, classified.Category);
        Assert.Equal("Couldn't read photo.jpg.", classified.Message);
        Assert.Same(exception, classified.Exception);
    }

    [Fact]
    public void Classify_AggregateException_IsUnwrapped()
    {
        var inner = Ai(AiFailureKind.Unreachable);

        var classified = classifier.Classify(new AggregateException(new AggregateException(inner)));

        Assert.True(classified.IsProviderFailure);
        Assert.Equal(inner.Message, classified.Message);
    }

    [Fact]
    public void Classify_ModuleClassifier_RunsBeforeTheSystemRules()
    {
        // A locked import file is an IOException to the system rules, but the module knows better.
        var classifierWithOverride = new ErrorClassifier([new FixedClassifier(ErrorCategory.Configuration)]);

        Assert.Equal(ErrorCategory.Configuration, classifierWithOverride.Classify(new IOException("x")).Category);
    }

    private static AiProviderException Ai(AiFailureKind kind, Exception? inner = null) =>
        new(kind, Provider, $"{kind} from {Provider}", innerException: inner);

    private static T Uninitialized<T>() => (T)RuntimeHelpers.GetUninitializedObject(typeof(T));

    private sealed class FixedClassifier(ErrorCategory category) : IExceptionClassifier
    {
        public ClassifiedError? TryClassify(Exception exception) => new(category, exception.Message, exception);
    }
}
