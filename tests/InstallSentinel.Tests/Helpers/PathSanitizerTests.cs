namespace InstallSentinel.Tests.Helpers;

using FluentAssertions;
using InstallSentinel.Common.Helpers;
using Xunit;

public class PathSanitizerTests
{
    [Theory]
    [InlineData(null, null)]
    [InlineData("", "")]
    [InlineData("  ", "  ")]
    public void NormalizePath_NullOrEmpty_ReturnsSame(string? input, string? expected)
    {
        var result = PathSanitizer.NormalizePath(input!);
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData(@"C:\Windows\notepad.exe")]
    [InlineData(@"D:\Projects\test.cs")]
    [InlineData(@"\\server\share\file.txt")]
    public void NormalizePath_NonDevicePath_ReturnsUnchanged(string input)
    {
        var result = PathSanitizer.NormalizePath(input);
        result.Should().Be(input);
    }

    [Fact]
    public void NormalizePath_DevicePath_ReturnsOriginalIfNoVolumeMapping()
    {
        var kernelPath = @"\Device\HarddiskVolume999\Windows\test.exe";
        var result = PathSanitizer.NormalizePath(kernelPath);
        // Without a real volume mapping, it returns the original path
        result.Should().Be(kernelPath);
    }

    [Theory]
    [InlineData("C:\\VeryLongPath\\To\\Some\\Deep\\Nested\\File.txt")]
    [InlineData("D:\\a\\b\\c\\d\\e\\f\\g\\h\\i\\j\\k\\l\\m.txt")]
    public void TruncatePath_LongPath_TruncatesWithEllipsis(string path)
    {
        var result = PathSanitizer.TruncatePath(path, 30);
        result.Length.Should().BeLessOrEqualTo(30);
        result.Should().Contain("...");
    }

    [Theory]
    [InlineData("C:\\short.txt")]
    [InlineData("README.md")]
    public void TruncatePath_ShortPath_ReturnsUnchanged(string path)
    {
        var result = PathSanitizer.TruncatePath(path, 80);
        result.Should().Be(path);
    }

    [Fact]
    public void TruncatePath_TwoPartPath_TruncatesFromEnd()
    {
        var path = @"C:\filename_with_really_long_name_that_exceeds_limit.txt";
        var result = PathSanitizer.TruncatePath(path, 30);
        result.Length.Should().BeLessOrEqualTo(30);
    }

    [Fact]
    public void GetShortPath_DelegatesToTruncatePath()
    {
        var path = @"C:\Very\Long\Path\That\Needs\Truncation\With\Many\Segments\file.txt";
        var result = PathSanitizer.GetShortPath(path, 40);
        var expected = PathSanitizer.TruncatePath(path, 40);
        result.Should().Be(expected);
    }

    [Fact]
    public void GetShortPath_DefaultMaxLength_Is80()
    {
        var shortPath = "C:\\test.txt";
        var result = PathSanitizer.GetShortPath(shortPath);
        result.Should().Be(shortPath);
    }
}
