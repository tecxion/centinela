namespace CamaraWin.Core.Tests;

static class OnvifSamples
{
    const string Head = """<s:Envelope xmlns:s="http://www.w3.org/2003/05/soap-envelope" xmlns:tds="http://www.onvif.org/ver10/device/wsdl" xmlns:trt="http://www.onvif.org/ver10/media/wsdl" xmlns:tt="http://www.onvif.org/ver10/schema"><s:Body>""";
    const string Tail = "</s:Body></s:Envelope>";

    public const string DeviceInformation = Head + """
        <tds:GetDeviceInformationResponse><tds:Manufacturer>tp-link</tds:Manufacturer><tds:Model>C200</tds:Model>
        <tds:FirmwareVersion>1.3.6</tds:FirmwareVersion><tds:SerialNumber>x</tds:SerialNumber><tds:HardwareId>2.0</tds:HardwareId>
        </tds:GetDeviceInformationResponse>
        """ + Tail;

    public const string Capabilities = Head + """
        <tds:GetCapabilitiesResponse><tds:Capabilities><tt:Media><tt:XAddr>http://192.168.1.20:2020/onvif/service</tt:XAddr>
        <tt:StreamingCapabilities><tt:RTPMulticast>false</tt:RTPMulticast></tt:StreamingCapabilities></tt:Media></tds:Capabilities>
        </tds:GetCapabilitiesResponse>
        """ + Tail;

    public const string Profiles = Head + """
        <trt:GetProfilesResponse>
          <trt:Profiles token="profile_1" fixed="true"><tt:Name>mainStream</tt:Name>
            <tt:VideoSourceConfiguration token="vsc"><tt:Bounds x="0" y="0" width="1920" height="1080"/></tt:VideoSourceConfiguration>
            <tt:VideoEncoderConfiguration token="main"><tt:Encoding>H264</tt:Encoding>
              <tt:Resolution><tt:Width>1920</tt:Width><tt:Height>1080</tt:Height></tt:Resolution></tt:VideoEncoderConfiguration>
          </trt:Profiles>
          <trt:Profiles token="profile_2" fixed="true"><tt:Name>minorStream</tt:Name>
            <tt:VideoEncoderConfiguration token="minor"><tt:Encoding>H264</tt:Encoding>
              <tt:Resolution><tt:Width>640</tt:Width><tt:Height>360</tt:Height></tt:Resolution></tt:VideoEncoderConfiguration>
          </trt:Profiles>
        </trt:GetProfilesResponse>
        """ + Tail;

    public static string StreamUri(string uri) => Head + $"""
        <trt:GetStreamUriResponse><trt:MediaUri><tt:Uri>{uri}</tt:Uri><tt:InvalidAfterConnect>false</tt:InvalidAfterConnect>
        <tt:InvalidAfterReboot>false</tt:InvalidAfterReboot><tt:Timeout>PT0S</tt:Timeout></trt:MediaUri></trt:GetStreamUriResponse>
        """ + Tail;

    public const string NotAuthorizedFault = """
        <s:Envelope xmlns:s="http://www.w3.org/2003/05/soap-envelope" xmlns:ter="http://www.onvif.org/ver10/error"><s:Body><s:Fault>
        <s:Code><s:Value>s:Sender</s:Value><s:Subcode><s:Value>ter:NotAuthorized</s:Value></s:Subcode></s:Code>
        <s:Reason><s:Text xml:lang="en">Sender not Authorized</s:Text></s:Reason></s:Fault></s:Body></s:Envelope>
        """;
}
