using SkiaSharp;
using ZXing;
using ZXing.Common;

return Verify(args);

static int Verify(string[] arguments)
{
    if (arguments.Length < 2)
    {
        Console.Error.WriteLine("用法：LabelFrame.PrintImageVerifier <PNG 目录> <预期条码 1> [预期条码 2 ...]");
        return 2;
    }

    var directory = Path.GetFullPath(arguments[0]);
    if (!Directory.Exists(directory))
    {
        Console.Error.WriteLine($"PNG 目录不存在：{directory}");
        return 2;
    }

    var expectedCodes = arguments[1..];
    var imagePaths = Directory
        .GetFiles(directory, "label-*.png", SearchOption.TopDirectoryOnly)
        .OrderBy(ParseLabelIndex)
        .ToArray();

    if (imagePaths.Length != expectedCodes.Length)
    {
        Console.Error.WriteLine($"PNG 数量与预期条码数量不一致：{imagePaths.Length}/{expectedCodes.Length}");
        return 1;
    }

    var reader = new BarcodeReaderGeneric
    {
        AutoRotate = true,
        Options = new DecodingOptions
        {
            TryHarder = true,
            PossibleFormats = [BarcodeFormat.CODE_128],
        },
    };

    for (var index = 0; index < imagePaths.Length; index++)
    {
        var path = imagePaths[index];
        using var bitmap = SKBitmap.Decode(path);
        if (bitmap is null || bitmap.Width == 0 || bitmap.Height == 0)
        {
            Console.Error.WriteLine($"PNG 无法解码或尺寸为空：{path}");
            return 1;
        }

        var inkPixels = CountInkPixels(bitmap);
        if (inkPixels == 0)
        {
            Console.Error.WriteLine($"PNG 为空白图片：{path}");
            return 1;
        }

        var luminance = new RGBLuminanceSource(
            bitmap.Bytes,
            bitmap.Width,
            bitmap.Height,
            RGBLuminanceSource.BitmapFormat.BGRA32);
        var decoded = reader.Decode(luminance)?.Text;
        if (!string.Equals(decoded, expectedCodes[index], StringComparison.Ordinal))
        {
            Console.Error.WriteLine($"条码内容不匹配：{Path.GetFileName(path)}，实际「{decoded ?? "<未解码>"}」，预期「{expectedCodes[index]}」");
            return 1;
        }

        Console.WriteLine($"PASS {Path.GetFileName(path)}：{decoded}，尺寸 {bitmap.Width}x{bitmap.Height}，墨迹像素 {inkPixels}");
    }

    return 0;
}

static int ParseLabelIndex(string path)
{
    var stem = Path.GetFileNameWithoutExtension(path);
    return int.TryParse(stem.AsSpan("label-".Length), out var index) ? index : int.MaxValue;
}

static int CountInkPixels(SKBitmap bitmap)
{
    var count = 0;
    for (var y = 0; y < bitmap.Height; y++)
    {
        for (var x = 0; x < bitmap.Width; x++)
        {
            var color = bitmap.GetPixel(x, y);
            if (color.Red < 245 || color.Green < 245 || color.Blue < 245)
            {
                count++;
            }
        }
    }

    return count;
}
