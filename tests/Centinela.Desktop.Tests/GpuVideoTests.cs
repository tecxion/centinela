using Centinela.Desktop.Video;

namespace Centinela.Desktop.Tests;

public class GpuVideoTests
{
    [Theory]
    [InlineData("llvmpipe (LLVM 15.0.7, 256 bits)")]
    [InlineData("ANGLE (Microsoft, Microsoft Basic Render Driver Direct3D11 vs_5_0 ps_5_0, D3D11)")]
    [InlineData("ANGLE (Google, Vulkan 1.3.0 (SwiftShader Device (Subzero) (0x0000C0DE)), SwiftShader driver)")]
    [InlineData("Apple Software Renderer")]
    [InlineData("softpipe")]
    [InlineData(null)]
    public void Software_renderers_do_not_count(string? renderer) => Assert.True(GpuVideo.IsSoftwareRenderer(renderer));

    [Theory]
    [InlineData("ANGLE (NVIDIA, NVIDIA GeForce RTX 3070 Direct3D11 vs_5_0 ps_5_0, D3D11)")]
    [InlineData("ANGLE (Intel, Intel(R) UHD Graphics 620 Direct3D11 vs_5_0 ps_5_0, D3D11)")]
    [InlineData("Apple M4 Pro")]
    [InlineData("AMD Radeon Graphics (radeonsi, renoir, LLVM 15.0.7, DRM 3.49, 6.1.0)")]
    [InlineData("Mesa Intel(R) UHD Graphics 620 (KBL GT2)")]
    [InlineData("SVGA3D; build: RELEASE; LLVM;")]
    public void Graphics_cards_count(string renderer) => Assert.False(GpuVideo.IsSoftwareRenderer(renderer));
}
