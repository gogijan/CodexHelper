using System.Globalization;
using System.Windows.Data;
using CodexHelper.Infrastructure;

namespace CodexHelper.Tests;

[TestClass]
public sealed class MinimumThumbViewportConverterTests
{
    [TestMethod]
    public void Convert_ReturnsBindingDoNothingForInvalidInputs()
    {
        var converter = new MinimumThumbViewportConverter();

        var result = converter.Convert([100d, 0d, 100d], typeof(double), "20", CultureInfo.InvariantCulture);

        Assert.AreSame(Binding.DoNothing, result);
    }

    [TestMethod]
    public void Convert_ReturnsOriginalViewportWhenNaturalThumbIsLargeEnough()
    {
        var converter = new MinimumThumbViewportConverter();

        var result = converter.Convert([100d, 0d, 100d, 100d], typeof(double), "20", CultureInfo.InvariantCulture);

        Assert.AreEqual(100d, (double)result);
    }

    [TestMethod]
    public void Convert_IncreasesViewportWhenNaturalThumbWouldBeTooSmall()
    {
        var converter = new MinimumThumbViewportConverter();

        var result = converter.Convert([1d, 0d, 999d, 100d], typeof(double), 20d, CultureInfo.InvariantCulture);

        Assert.AreEqual(249.75d, (double)result, 0.0001d);
    }

    [TestMethod]
    public void Convert_ReturnsLargeViewportWhenTrackIsSmallerThanMinimumThumb()
    {
        var converter = new MinimumThumbViewportConverter();

        var result = converter.Convert([10d, 5d, 25d, 20d], typeof(double), 20d, CultureInfo.InvariantCulture);

        Assert.AreEqual(20000d, (double)result);
    }

    [TestMethod]
    public void ConvertBack_IsNotSupported()
    {
        var converter = new MinimumThumbViewportConverter();

        Assert.ThrowsException<NotSupportedException>(() =>
            converter.ConvertBack(1d, [typeof(double)], "20", CultureInfo.InvariantCulture));
    }
}
