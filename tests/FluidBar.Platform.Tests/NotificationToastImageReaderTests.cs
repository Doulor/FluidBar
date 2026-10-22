using FluidBar.Platform.Windows.Notifications;
using Xunit;

namespace FluidBar.Platform.Tests;

public sealed class NotificationToastImageReaderTests
{
    [Fact]
    public void FindImageSource_PrefersAppLogoOverride_AndSkipsEmptySources()
    {
        const string xml = """
            <toast>
              <visual>
                <binding template="ToastGeneric">
                  <image placement="hero" src="D:\pictures\hero.jpg"/>
                  <image placement="appLogoOverride" hint-crop="circle" src="D:\Doulor\QQ\Tencent Files\2737855297\nt_qq\nt_data\avatar\group\1b\s_avatar"/>
                  <image placement="appLogoOverride" src=""/>
                </binding>
              </visual>
            </toast>
            """;

        Assert.Equal(
            @"D:\Doulor\QQ\Tencent Files\2737855297\nt_qq\nt_data\avatar\group\1b\s_avatar",
            NotificationToastImageReader.FindImageSource(xml));
    }

    [Fact]
    public void FindImageSource_FallsBackToFirstRealImage()
    {
        const string xml = """
            <toast>
              <visual>
                <binding template="ToastGeneric">
                  <image src="D:\pictures\hero.jpg"/>
                  <image src=""/>
                </binding>
              </visual>
            </toast>
            """;

        Assert.Equal(@"D:\pictures\hero.jpg", NotificationToastImageReader.FindImageSource(xml));
    }
}
