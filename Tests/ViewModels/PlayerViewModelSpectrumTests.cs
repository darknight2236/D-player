using Microsoft.Extensions.Options;
using NSubstitute;
using UmaPlayer.Configuration;
using UmaPlayer.Models;
using UmaPlayer.Services;
using UmaPlayer.ViewModels;
using Xunit;

namespace UmaPlayer.Tests.ViewModels;

public class PlayerViewModelSpectrumTests
{
    private readonly IPlaybackService _player = Substitute.For<IPlaybackService>();
    private readonly ISettingsPersistence _persistence = Substitute.For<ISettingsPersistence>();
    private readonly IOptions<AppSettings> _options = Options.Create(new AppSettings());

    public PlayerViewModelSpectrumTests()
    {
        _persistence.LoadAsync().Returns(Task.FromResult(new AppSettings()));

        // SpectrumConfig 为 record（引用类型），NSubstitute 默认返回 null；
        // 需要预设一个实例以避免 OnSpectrumEnabledChanged 中的 NRE
        _player.SpectrumConfig.Returns(new SpectrumConfig());
    }

    private PlayerViewModel CreateVm()
    {
        return new PlayerViewModel(_player, _persistence, _options);
    }

    [Fact]
    public void SpectrumData_WhenSpectrumEnabled_UpdatesProperty()
    {
        // Arrange
        var vm = CreateVm();
        vm.SpectrumEnabled = true;
        // SampleAggregator 已将 FFT bins 对数映射为 32 bars，传入的数据就是 32 元素
        var testData = new float[32];
        Array.Fill(testData, 0.5f);

        // Act
        _player.SpectrumDataAvailable += Raise.Event<Action<float[]>>(testData);

        // Assert
        Assert.Equal(32, vm.SpectrumData.Length);
        Assert.Contains(vm.SpectrumData, x => x > 0);
    }

    [Fact]
    public void SpectrumData_WhenSpectrumDisabled_DoesNotUpdate()
    {
        // Arrange
        var vm = CreateVm();
        vm.SpectrumEnabled = false;
        var testData = new float[32];
        Array.Fill(testData, 0.5f);

        // Act
        _player.SpectrumDataAvailable += Raise.Event<Action<float[]>>(testData);

        // Assert
        Assert.All(vm.SpectrumData, x => Assert.Equal(0f, x));
    }

    [Fact]
    public void ToggleSpectrum_TogglesEnabled()
    {
        // Arrange
        var vm = CreateVm();
        vm.SpectrumEnabled = true;

        // Act
        vm.ToggleSpectrumCommand.Execute(null);

        // Assert
        Assert.False(vm.SpectrumEnabled);
    }

    [Fact]
    public void SpectrumSettings_Changed_PersistsToSettings()
    {
        // Arrange
        var vm = CreateVm();

        // Act
        vm.SpectrumSensitivity = 1.5;

        // Assert
        _persistence.Received().UpdateAsync(Arg.Is<Func<AppSettings, AppSettings>>(fn =>
            fn(new AppSettings()).SpectrumSensitivity == 1.5));
    }

    [Fact]
    public void SpectrumColorTheme_ValidRange_NoThrow()
    {
        // Arrange
        var vm = CreateVm();

        // Act & Assert
        vm.SpectrumColorTheme = 0; // Purple
        vm.SpectrumColorTheme = 1; // Blue
        vm.SpectrumColorTheme = 2; // Green
        vm.SpectrumColorTheme = 3; // Rainbow
    }
}
