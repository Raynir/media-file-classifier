using System.Text;

namespace MediaFileClassifier.Infrastructure.Tests.AI.Onnx;

/// <summary>
/// Writes a minimal ONNX model, <c>y = Identity(x)</c> on a float tensor of shape [1, width], by encoding the
/// protobuf directly, so the tests need no model file and no ONNX tooling.
/// </summary>
internal static class TinyOnnxModel
{
    public const string InputName = "x";

    public const string OutputName = "y";

    /// <summary>
    /// Writes the model to a file.
    /// </summary>
    /// <param name="path">File to write.</param>
    /// <param name="width">Length of the vector.</param>
    public static void WriteIdentity(string path, int width)
    {
        var node = Concat(String(1, InputName), String(2, OutputName), String(4, "Identity"));
        var graph = Concat(
            Message(1, node),
            String(2, "tiny"),
            Message(11, ValueInfo(InputName, width)),
            Message(12, ValueInfo(OutputName, width)));
        var model = Concat(
            Varint(1, 8), // ir_version
            Message(7, graph),
            Message(8, Varint(2, 13))); // opset_import { version: 13 }
        File.WriteAllBytes(path, model);
    }

    private static byte[] ValueInfo(string name, int width)
    {
        var shape = Concat(Message(1, Varint(1, 1)), Message(1, Varint(1, (ulong)width)));
        var tensorType = Concat(Varint(1, 1), Message(2, shape)); // elem_type FLOAT, shape
        return Concat(String(1, name), Message(2, Message(1, tensorType)));
    }

    private static byte[] Varint(int field, ulong value) => Concat(Key(field, 0), RawVarint(value));

    private static byte[] String(int field, string value) => Message(field, Encoding.UTF8.GetBytes(value));

    private static byte[] Message(int field, byte[] body) => Concat(Key(field, 2), RawVarint((ulong)body.Length), body);

    private static byte[] Key(int field, int wireType) => RawVarint((ulong)((field << 3) | wireType));

    private static byte[] RawVarint(ulong value)
    {
        var bytes = new List<byte>();
        do
        {
            var current = (byte)(value & 0x7F);
            value >>= 7;
            bytes.Add(value == 0 ? current : (byte)(current | 0x80));
        }
        while (value != 0);

        return [.. bytes];
    }

    private static byte[] Concat(params byte[][] parts) => [.. parts.SelectMany(part => part)];
}
