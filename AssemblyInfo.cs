using System.Windows;

// 告诉 WPF 主题资源查找规则：
//   - 主题特定字典 (Aero/Luna 等)：None —— 不使用 OS 主题字典；
//   - 通用字典 (themes/generic.xaml)：SourceAssembly —— 在本程序集内查找。
// 本项目自定义主题资源在 Themes/*.xaml，由 App.xaml 合并加载，因此该设置实际不发生作用，
// 保留是 WPF 项目模板的标准配置。
[assembly: ThemeInfo(
    ResourceDictionaryLocation.None,
    ResourceDictionaryLocation.SourceAssembly
)]
