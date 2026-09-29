using System.Xml.Linq;
using Centinela.Core.Onvif;

namespace Centinela.Core.Tests;

public class EncoderChangeBuilderTests
{
    const string Configurations = """
        <s:Envelope xmlns:s="http://www.w3.org/2003/05/soap-envelope" xmlns:trt="http://www.onvif.org/ver10/media/wsdl" xmlns:tt="http://www.onvif.org/ver10/schema"><s:Body>
        <trt:GetVideoEncoderConfigurationsResponse>
          <trt:Configurations token="VideoEncoder000"><tt:Name>VideoEncoder000</tt:Name><tt:UseCount>1</tt:UseCount><tt:Encoding>H264</tt:Encoding>
            <tt:Resolution><tt:Width>2560</tt:Width><tt:Height>1440</tt:Height></tt:Resolution><tt:Quality>4</tt:Quality>
            <tt:RateControl><tt:FrameRateLimit>12</tt:FrameRateLimit><tt:EncodingInterval>1</tt:EncodingInterval><tt:BitrateLimit>1536</tt:BitrateLimit></tt:RateControl>
            <tt:H264><tt:GovLength>48</tt:GovLength><tt:H264Profile>Main</tt:H264Profile></tt:H264>
            <tt:Multicast><tt:Address><tt:Type>IPv4</tt:Type><tt:IPv4Address>0.0.0.0</tt:IPv4Address></tt:Address><tt:Port>0</tt:Port><tt:TTL>0</tt:TTL><tt:AutoStart>false</tt:AutoStart></tt:Multicast>
            <tt:SessionTimeout>PT60S</tt:SessionTimeout></trt:Configurations>
          <trt:Configurations token="VideoEncoder001"><tt:Name>VideoEncoder001</tt:Name><tt:Encoding>H264</tt:Encoding>
            <tt:Resolution><tt:Width>640</tt:Width><tt:Height>480</tt:Height></tt:Resolution>
            <tt:RateControl><tt:FrameRateLimit>15</tt:FrameRateLimit><tt:BitrateLimit>512</tt:BitrateLimit></tt:RateControl></trt:Configurations>
        </trt:GetVideoEncoderConfigurationsResponse></s:Body></s:Envelope>
        """;

    static readonly XNamespace Tt = "http://www.onvif.org/ver10/schema";
    static readonly XNamespace Trt = "http://www.onvif.org/ver10/media/wsdl";

    [Fact]
    public void Changes_resolution_fps_and_bitrate_and_keeps_everything_else()
    {
        var body = XElement.Parse(EncoderChangeBuilder.BuildSetConfiguration(Configurations, "VideoEncoder000",
            new EncoderChange(1280, 720, 10, 768)));

        Assert.Equal(Trt + "SetVideoEncoderConfiguration", body.Name);
        var config = body.Element(Trt + "Configuration")!;
        Assert.Equal("VideoEncoder000", (string?)config.Attribute("token"));
        Assert.Equal("1280", config.Element(Tt + "Resolution")!.Element(Tt + "Width")!.Value);
        Assert.Equal("720", config.Element(Tt + "Resolution")!.Element(Tt + "Height")!.Value);
        var rate = config.Element(Tt + "RateControl")!;
        Assert.Equal(("10", "1", "768"),
            (rate.Element(Tt + "FrameRateLimit")!.Value, rate.Element(Tt + "EncodingInterval")!.Value, rate.Element(Tt + "BitrateLimit")!.Value));
        // Keyframe every 4 s before (48 at 12 fps) and after (40 at 10 fps).
        Assert.Equal("40", config.Element(Tt + "H264")!.Element(Tt + "GovLength")!.Value);
        Assert.Equal(("4", "Main", "PT60S", "0.0.0.0"),
            (config.Element(Tt + "Quality")!.Value, config.Element(Tt + "H264")!.Element(Tt + "H264Profile")!.Value,
             config.Element(Tt + "SessionTimeout")!.Value, config.Descendants(Tt + "IPv4Address").Single().Value));
        Assert.Equal("true", body.Element(Trt + "ForcePersistence")!.Value);
    }

    [Fact]
    public void Only_the_requested_configuration_is_sent()
    {
        var body = XElement.Parse(EncoderChangeBuilder.BuildSetConfiguration(Configurations, "VideoEncoder001",
            new EncoderChange(640, 360, 10, 256)));
        var config = Assert.Single(body.Elements(Trt + "Configuration"));
        Assert.Equal("VideoEncoder001", (string?)config.Attribute("token"));
        Assert.Equal("256", config.Descendants(Tt + "BitrateLimit").Single().Value);
    }

    [Fact]
    public void Unknown_token_is_an_onvif_error() =>
        Assert.Throws<OnvifException>(() =>
            EncoderChangeBuilder.BuildSetConfiguration(Configurations, "Nope", new EncoderChange(640, 360, 10, 256)));

    [Fact]
    public void Fault_reason_reads_text_and_subcodes()
    {
        const string fault = """
            <s:Envelope xmlns:s="http://www.w3.org/2003/05/soap-envelope" xmlns:ter="http://www.onvif.org/ver10/error"><s:Body><s:Fault>
              <s:Code><s:Value>s:Sender</s:Value><s:Subcode><s:Value>ter:InvalidArgVal</s:Value><s:Subcode><s:Value>ter:ConfigModify</s:Value></s:Subcode></s:Subcode></s:Code>
              <s:Reason><s:Text xml:lang="en">The configuration parameters are not possible to set</s:Text></s:Reason>
            </s:Fault></s:Body></s:Envelope>
            """;
        Assert.Equal("The configuration parameters are not possible to set — ter:InvalidArgVal / ter:ConfigModify",
            EncoderChangeBuilder.FaultReason(fault));
        Assert.Null(EncoderChangeBuilder.FaultReason("<ok/>"));
        Assert.Null(EncoderChangeBuilder.FaultReason("not xml"));
    }
}
