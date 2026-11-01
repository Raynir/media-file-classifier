using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using MetadataDirectory = MetadataExtractor.Directory;

namespace MediaFileClassifier.Infrastructure.Media.Metadata;

/// <summary>
/// Writes the full metadata dump stored in <see cref="Domain.Metadata.TechnicalMetadata.RawMetadataJson"/>
/// (E01-S02-T04): one object per directory, with each tag's name and description. It holds no file paths, because
/// the readers get streams (library packages scrub paths, E07-S08).
/// </summary>
internal static class RawMetadataJson
{
    private static readonly JsonWriterOptions WriterOptions = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    public static string FromDirectories(IEnumerable<MetadataDirectory> directories)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, WriterOptions))
        {
            writer.WriteStartObject();
            var names = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var directory in directories)
            {
                // Some formats repeat a directory, e.g. one per PNG text chunk; number the repeats.
                var count = names[directory.Name] = names.GetValueOrDefault(directory.Name) + 1;
                writer.WriteStartObject(count == 1 ? directory.Name : $"{directory.Name} ({count})");
                foreach (var tag in directory.Tags)
                {
                    writer.WriteString(tag.Name, tag.Description);
                }

                if (directory.HasError)
                {
                    writer.WriteStartArray("(errors)");
                    foreach (var error in directory.Errors)
                    {
                        writer.WriteStringValue(error);
                    }

                    writer.WriteEndArray();
                }

                writer.WriteEndObject();
            }

            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }
}
