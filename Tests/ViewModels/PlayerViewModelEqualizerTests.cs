using Microsoft.Extensions.Options;
using NSubstitute;
using DPlayer.Configuration;
using DPlayer.Models;
using DPlayer.Services;
using DPlayer.ViewModels;
using Xunit;

namespace DPlayer.Tests.ViewModels;

public class PlayerViewModelEqualizerTests
{
    private readonly IPlaybackService _player = Substitute.For<IPlaybackService>();
    private readonly ISettingsPersistence _persistence = Substitute.For<ISettingsPersistence>();
    private readonly IOptions<AppSettings> _options = Options.Create(new AppSettings());

    public PlayerViewModelEqualizerTests()
    {
        _persistence.LoadAsync().Returns(Task.FromResult(new AppSettings()));
        // EqualizerConfig 为 record，NSubstitute 默认返回 null → 预设实例避免 with 表达式 NRE
        _player.EqualizerConfig.Returns(new EqualizerConfig());
    }

    private PlayerViewModel CreateVm(AppSettings? settings = null)
    {
        if (settings != null)
            _persistence.LoadAsync().Returns(Task.FromResult(settings));
        return new PlayerViewModel(_player, _persistence, _options);
    }

    [Fact]
    public void Ctor_LoadsEqualizerEnabled_FromSettings()
    {
        var vm = CreateVm(new AppSettings { EqualizerEnabled = true });
        Assert.True(vm.EqualizerEnabled);
    }

    [Fact]
    public void Ctor_AppliesPersistedConfig_ToPlaybackService()
    {
        var settings = new AppSettings
        {
            EqualizerEnabled = true,
            EqualizerPreamp = -3,
            EqualizerBands = new double[] { 5, 4, 3, 1, -1, -1, 0, 2, 3, 4 },
            EqualizerPreset = "Rock"
        };
        CreateVm(settings);
        _player.Received().EqualizerConfig = Arg.Is<EqualizerConfig>(c =>
            c.Enabled && c.PreampDb == -3 && c.Preset == "Rock" && c.BandGainsDb[0] == 5);
        // 构造期 _isInitializing=true → OnEqualizerEnabledChanged 不应写盘
        _persistence.DidNotReceive().UpdateAsync(Arg.Any<Func<AppSettings, AppSettings>>());
    }

    [Fact]
    public void EqualizerEnabled_Changed_PropagatesToService_AndPersists()
    {
        var vm = CreateVm();
        vm.EqualizerEnabled = true;
        _player.Received().EqualizerConfig = Arg.Is<EqualizerConfig>(c => c.Enabled);
        _persistence.Received().UpdateAsync(Arg.Is<Func<AppSettings, AppSettings>>(fn =>
            fn(new AppSettings()).EqualizerEnabled));
    }
}
