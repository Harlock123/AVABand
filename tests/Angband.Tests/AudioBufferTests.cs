using Angband.Audio;

namespace Angband.Tests;

/// <summary>The audio buffer setting (bigger OpenAL mixing periods keep virtual machines' sound smooth).</summary>
public class AudioBufferTests
{
    [Theory]
    [InlineData(AudioBuffer.Small, false, 0)]
    [InlineData(AudioBuffer.Small, true, 0)]
    [InlineData(AudioBuffer.Medium, false, 1024)]
    [InlineData(AudioBuffer.Large, false, 2048)]
    [InlineData(AudioBuffer.Automatic, false, 0)]
    [InlineData(AudioBuffer.Automatic, true, 2048)] // a VM's emulated sound card needs the room
    [InlineData(null, true, 2048)]
    [InlineData("nonsense", false, 0)]
    public void TheChoice_GivesTheMixingPeriod(string? choice, bool vm, int frames) =>
        Assert.Equal(frames, AudioBuffer.PeriodFrames(choice, vm));

    [Fact]
    public void EveryChoiceHasALabel() =>
        Assert.Equal([AudioBuffer.Automatic, AudioBuffer.Small, AudioBuffer.Medium, AudioBuffer.Large], AudioBuffer.Choices.Select(c => c.Id));

    [Fact]
    public void VirtualMachineDetection_NeverThrows() => _ = VirtualMachine.Detect();
}
