using Centinela.Core.Onvif;

namespace Centinela.Core.Tests;

public class EncoderSettingsParserTests
{
    [Fact]
    public void Onvif_configurations_give_resolution_fps_bitrate_and_gop()
    {
        const string xml = """
            <s:Envelope xmlns:s="http://www.w3.org/2003/05/soap-envelope" xmlns:trt="http://www.onvif.org/ver10/media/wsdl" xmlns:tt="http://www.onvif.org/ver10/schema"><s:Body>
            <trt:GetVideoEncoderConfigurationsResponse>
              <trt:Configurations token="000"><tt:Name>MainStream</tt:Name><tt:UseCount>1</tt:UseCount><tt:Encoding>H264</tt:Encoding>
                <tt:Resolution><tt:Width>2560</tt:Width><tt:Height>1440</tt:Height></tt:Resolution><tt:Quality>4</tt:Quality>
                <tt:RateControl><tt:FrameRateLimit>15</tt:FrameRateLimit><tt:EncodingInterval>1</tt:EncodingInterval><tt:BitrateLimit>2048</tt:BitrateLimit></tt:RateControl>
                <tt:H264><tt:GovLength>30</tt:GovLength><tt:H264Profile>Main</tt:H264Profile></tt:H264></trt:Configurations>
              <trt:Configurations token="001"><tt:Name>SubStream</tt:Name><tt:Encoding>H264</tt:Encoding>
                <tt:Resolution><tt:Width>640</tt:Width><tt:Height>360</tt:Height></tt:Resolution>
                <tt:RateControl><tt:FrameRateLimit>15</tt:FrameRateLimit><tt:BitrateLimit>512</tt:BitrateLimit></tt:RateControl></trt:Configurations>
            </trt:GetVideoEncoderConfigurationsResponse></s:Body></s:Envelope>
            """;
        var configs = EncoderSettingsParser.ParseOnvifConfigurations(xml);
        Assert.Equal(2, configs.Count);
        Assert.Equal(("000", new EncoderSettings("MainStream", "H264", 2560, 1440, 15, 2048, null, 30)), configs[0]);
        Assert.Equal(("001", new EncoderSettings("SubStream", "H264", 640, 360, 15, 512, null, 0)), configs[1]);
    }

    [Fact]
    public void Onvif_options_give_resolutions_and_ranges()
    {
        const string xml = """
            <s:Envelope xmlns:s="http://www.w3.org/2003/05/soap-envelope" xmlns:trt="http://www.onvif.org/ver10/media/wsdl" xmlns:tt="http://www.onvif.org/ver10/schema"><s:Body>
            <trt:GetVideoEncoderConfigurationOptionsResponse><trt:Options>
              <tt:QualityRange><tt:Min>1</tt:Min><tt:Max>6</tt:Max></tt:QualityRange>
              <tt:H264><tt:ResolutionsAvailable><tt:Width>1280</tt:Width><tt:Height>720</tt:Height></tt:ResolutionsAvailable>
                <tt:ResolutionsAvailable><tt:Width>2560</tt:Width><tt:Height>1440</tt:Height></tt:ResolutionsAvailable>
                <tt:FrameRateRange><tt:Min>1</tt:Min><tt:Max>25</tt:Max></tt:FrameRateRange></tt:H264>
              <tt:Extension><tt:H264><tt:BitrateRange><tt:Min>32</tt:Min><tt:Max>6144</tt:Max></tt:BitrateRange></tt:H264></tt:Extension>
            </trt:Options></trt:GetVideoEncoderConfigurationOptionsResponse></s:Body></s:Envelope>
            """;
        var options = EncoderSettingsParser.ParseOnvifOptions(xml);
        Assert.Equal([(2560, 1440), (1280, 720)], options.Resolutions);
        Assert.Equal((1, 25, 32, 6144), (options.FpsMin, options.FpsMax, options.BitrateMinKbps, options.BitrateMaxKbps));
    }

    [Fact]
    public void Dahua_encode_config_gives_main_and_sub_streams()
    {
        const string text = """
            table.Encode[0].MainFormat[0].AudioEnable=true
            table.Encode[0].MainFormat[0].Video.BitRate=2048
            table.Encode[0].MainFormat[0].Video.BitRateControl=VBR
            table.Encode[0].MainFormat[0].Video.Compression=H.265
            table.Encode[0].MainFormat[0].Video.FPS=15.000000
            table.Encode[0].MainFormat[0].Video.GOP=30
            table.Encode[0].MainFormat[0].Video.Height=1440
            table.Encode[0].MainFormat[0].Video.Width=2560
            table.Encode[0].ExtraFormat[0].Video.BitRate=384
            table.Encode[0].ExtraFormat[0].Video.BitRateControl=CBR
            table.Encode[0].ExtraFormat[0].Video.Compression=H.264
            table.Encode[0].ExtraFormat[0].Video.FPS=15
            table.Encode[0].ExtraFormat[0].Video.Height=360
            table.Encode[0].ExtraFormat[0].Video.Width=640
            table.Encode[0].ExtraFormat[1].Video.BitRate=128
            """;
        var streams = EncoderSettingsParser.ParseDahuaEncode(text.Replace("\n", "\r\n"));
        Assert.Equal(
            [new EncoderSettings("Principal", "H.265", 2560, 1440, 15, 2048, "VBR", 30),
             new EncoderSettings("Secundario", "H.264", 640, 360, 15, 384, "CBR", 0)],
            streams);
    }

    [Fact]
    public void Dahua_text_without_encode_values_gives_nothing() =>
        Assert.Empty(EncoderSettingsParser.ParseDahuaEncode("Error\r\nBad Request!"));
}
