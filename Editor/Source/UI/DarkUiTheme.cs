using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;

namespace Gp200.Replica;

/// <summary>
/// Master AuraCode Dark-Cyan Studio theme for GP-200 Community Editor.
/// Completely overrides native Windows 11 WPF controls to prevent any white-on-white or black-on-black rendering,
/// while providing glowing cyan accents, smooth borders, and readable high-contrast palettes.
/// </summary>
public static class DarkUiTheme
{
    // AuraCode Dark-Cyan Palette
    public static readonly SolidColorBrush BackgroundBrush = new(Color.FromRgb(7, 11, 18));      // #070B12 (Deep)
    public static readonly SolidColorBrush PanelBrush = new(Color.FromRgb(13, 21, 36));          // #0D1524 (Panel)
    public static readonly SolidColorBrush CardBrush = new(Color.FromRgb(20, 32, 54));           // #142036 (Card)
    public static readonly SolidColorBrush BorderBrush = new(Color.FromArgb(56, 0, 229, 255));   // rgba(0, 229, 255, 0.22)
    public static readonly SolidColorBrush InputBrush = new(Color.FromRgb(11, 19, 36));          // #0B1324 (Input)
    public static readonly SolidColorBrush InputBorderBrush = new(Color.FromArgb(77, 0, 229, 255)); // rgba(0, 229, 255, 0.3)
    public static readonly SolidColorBrush TextBrush = new(Color.FromRgb(248, 250, 252));        // #F8FAFC (Text Main)
    public static readonly SolidColorBrush MutedBrush = new(Color.FromRgb(148, 163, 184));       // #94A3B8 (Text Muted)
    public static readonly SolidColorBrush CyanPrimaryBrush = new(Color.FromRgb(0, 229, 255));    // #00E5FF (Cyan Primary)
    public static readonly SolidColorBrush CyanSecondaryBrush = new(Color.FromRgb(6, 182, 212));  // #06B6D4 (Cyan Secondary)
    public static readonly SolidColorBrush AccentBrush = new(Color.FromRgb(0, 229, 255));         // #00E5FF
    public static readonly SolidColorBrush HighlightBrush = new(Color.FromRgb(0, 119, 182));     // #0077B6
    public static readonly SolidColorBrush SuccessBrush = new(Color.FromRgb(16, 185, 129));      // #10B981
    public static readonly SolidColorBrush WarningBrush = new(Color.FromRgb(245, 158, 11));      // #F59E0B
    public static readonly SolidColorBrush DangerBrush = new(Color.FromRgb(239, 68, 68));         // #EF4444

    public const string DarkStylesXaml = """
<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">

    <!-- Global system brushes (AuraCode Dark-Cyan) -->
    <SolidColorBrush x:Key="{x:Static SystemColors.WindowBrushKey}" Color="#070B12"/>
    <SolidColorBrush x:Key="{x:Static SystemColors.WindowTextBrushKey}" Color="#F8FAFC"/>
    <SolidColorBrush x:Key="{x:Static SystemColors.ControlBrushKey}" Color="#0D1524"/>
    <SolidColorBrush x:Key="{x:Static SystemColors.ControlTextBrushKey}" Color="#F8FAFC"/>
    <SolidColorBrush x:Key="{x:Static SystemColors.HighlightBrushKey}" Color="#0077B6"/>
    <SolidColorBrush x:Key="{x:Static SystemColors.HighlightTextBrushKey}" Color="#FFFFFF"/>
    <SolidColorBrush x:Key="{x:Static SystemColors.GrayTextBrushKey}" Color="#94A3B8"/>

    <!-- COMBOBOX TOGGLE BUTTON TEMPLATE (AuraCode Dark-Cyan) -->
    <ControlTemplate x:Key="DarkComboBoxToggleButton" TargetType="ToggleButton">
        <Border x:Name="TemplateRoot"
                Background="#0B1324"
                BorderBrush="#3300E5FF"
                BorderThickness="1"
                CornerRadius="6"
                SnapsToDevicePixels="True">
            <Grid>
                <Grid.ColumnDefinitions>
                    <ColumnDefinition Width="*"/>
                    <ColumnDefinition Width="28"/>
                </Grid.ColumnDefinitions>
                <Border Grid.Column="1" Background="Transparent" Margin="0,0,4,0">
                    <Path x:Name="Arrow"
                          Data="M 0 0 L 5 5 L 10 0 Z"
                          Fill="#00E5FF"
                          HorizontalAlignment="Center"
                          VerticalAlignment="Center"/>
                </Border>
            </Grid>
        </Border>
        <ControlTemplate.Triggers>
            <Trigger Property="IsMouseOver" Value="True">
                <Setter TargetName="TemplateRoot" Property="Background" Value="#142036"/>
                <Setter TargetName="TemplateRoot" Property="BorderBrush" Value="#00E5FF"/>
                <Setter TargetName="Arrow" Property="Fill" Value="#38BDF8"/>
            </Trigger>
            <Trigger Property="IsChecked" Value="True">
                <Setter TargetName="TemplateRoot" Property="BorderBrush" Value="#00E5FF"/>
            </Trigger>
            <Trigger Property="IsEnabled" Value="False">
                <Setter TargetName="TemplateRoot" Property="Opacity" Value="0.45"/>
            </Trigger>
        </ControlTemplate.Triggers>
    </ControlTemplate>

    <!-- COMBOBOX ITEM STYLE -->
    <Style TargetType="ComboBoxItem">
        <Setter Property="SnapsToDevicePixels" Value="True"/>
        <Setter Property="Padding" Value="10,7"/>
        <Setter Property="Background" Value="#0D1524"/>
        <Setter Property="Foreground" Value="#F8FAFC"/>
        <Setter Property="FontSize" Value="12"/>
        <Setter Property="HorizontalContentAlignment" Value="Stretch"/>
        <Setter Property="Template">
            <Setter.Value>
                <ControlTemplate TargetType="ComboBoxItem">
                    <Border x:Name="ItemBorder"
                            Background="{TemplateBinding Background}"
                            BorderThickness="0"
                            Padding="{TemplateBinding Padding}"
                            CornerRadius="4"
                            Margin="2,1">
                        <ContentPresenter HorizontalAlignment="Left" VerticalAlignment="Center"/>
                    </Border>
                    <ControlTemplate.Triggers>
                        <Trigger Property="IsHighlighted" Value="True">
                            <Setter TargetName="ItemBorder" Property="Background" Value="#142036"/>
                            <Setter Property="Foreground" Value="#00E5FF"/>
                        </Trigger>
                        <Trigger Property="IsSelected" Value="True">
                            <Setter TargetName="ItemBorder" Property="Background" Value="#0077B6"/>
                            <Setter Property="Foreground" Value="#FFFFFF"/>
                            <Setter Property="FontWeight" Value="SemiBold"/>
                        </Trigger>
                        <Trigger Property="IsEnabled" Value="False">
                            <Setter Property="Foreground" Value="#64748B"/>
                        </Trigger>
                    </ControlTemplate.Triggers>
                </ControlTemplate>
            </Setter.Value>
        </Setter>
    </Style>

    <!-- FULL COMBOBOX CONTROL TEMPLATE (NO NATIVE AERO2 WHITE TOGGLE BUTTON) -->
    <Style TargetType="ComboBox">
        <Setter Property="SnapsToDevicePixels" Value="True"/>
        <Setter Property="Foreground" Value="#F8FAFC"/>
        <Setter Property="Background" Value="#0B1324"/>
        <Setter Property="BorderBrush" Value="#3300E5FF"/>
        <Setter Property="BorderThickness" Value="1"/>
        <Setter Property="MinHeight" Value="32"/>
        <Setter Property="FontSize" Value="12"/>
        <Setter Property="Padding" Value="10,6"/>
        <Setter Property="ScrollViewer.HorizontalScrollBarVisibility" Value="Disabled"/>
        <Setter Property="ScrollViewer.VerticalScrollBarVisibility" Value="Auto"/>
        <Setter Property="Cursor" Value="Hand"/>
        <Setter Property="Template">
            <Setter.Value>
                <ControlTemplate TargetType="ComboBox">
                    <Grid x:Name="MainGrid" SnapsToDevicePixels="True">
                        <!-- Closed body: custom ToggleButton -->
                        <ToggleButton x:Name="ToggleButton"
                                      Template="{StaticResource DarkComboBoxToggleButton}"
                                      Focusable="False"
                                      ClickMode="Press"
                                      IsChecked="{Binding IsDropDownOpen, Mode=TwoWay, RelativeSource={RelativeSource TemplatedParent}}"/>
                        
                        <!-- Selected text display: ALWAYS crisp white on dark -->
                        <ContentPresenter x:Name="ContentSite"
                                          IsHitTestVisible="False"
                                          Content="{TemplateBinding SelectionBoxItem}"
                                          ContentTemplate="{TemplateBinding SelectionBoxItemTemplate}"
                                          ContentTemplateSelector="{TemplateBinding ItemTemplateSelector}"
                                          Margin="10,6,28,6"
                                          VerticalAlignment="Center"
                                          HorizontalAlignment="Left">
                            <ContentPresenter.Resources>
                                <Style TargetType="TextBlock">
                                    <Setter Property="Foreground" Value="#F8FAFC"/>
                                </Style>
                            </ContentPresenter.Resources>
                        </ContentPresenter>

                        <!-- Drop-down Popup: Dark background, no white edges -->
                        <Popup x:Name="PART_Popup"
                               AllowsTransparency="True"
                               IsOpen="{TemplateBinding IsDropDownOpen}"
                               Placement="Bottom"
                               PopupAnimation="Slide"
                               Focusable="False">
                            <Border x:Name="DropDownBorder"
                                    Background="#0D1524"
                                    BorderBrush="#00E5FF"
                                    BorderThickness="1"
                                    CornerRadius="6"
                                    Padding="4"
                                    MinWidth="{Binding ActualWidth, ElementName=MainGrid}"
                                    MaxHeight="320">
                                <ScrollViewer CanContentScroll="True">
                                    <ItemsPresenter KeyboardNavigation.DirectionalNavigation="Contained"/>
                                </ScrollViewer>
                            </Border>
                        </Popup>
                    </Grid>
                    <ControlTemplate.Triggers>
                        <Trigger Property="IsEnabled" Value="False">
                            <Setter Property="Opacity" Value="0.45"/>
                        </Trigger>
                    </ControlTemplate.Triggers>
                </ControlTemplate>
            </Setter.Value>
        </Setter>
    </Style>

    <!-- LISTBOX ITEM STYLE -->
    <Style TargetType="ListBoxItem">
        <Setter Property="SnapsToDevicePixels" Value="True"/>
        <Setter Property="Background" Value="Transparent"/>
        <Setter Property="Foreground" Value="#E2E8F0"/>
        <Setter Property="Padding" Value="9,6"/>
        <Setter Property="Margin" Value="1,1"/>
        <Setter Property="FontSize" Value="12"/>
        <Setter Property="HorizontalContentAlignment" Value="Stretch"/>
        <Setter Property="Template">
            <Setter.Value>
                <ControlTemplate TargetType="ListBoxItem">
                    <Border x:Name="Border"
                            Background="{TemplateBinding Background}"
                            BorderThickness="1"
                            BorderBrush="Transparent"
                            CornerRadius="5"
                            Padding="{TemplateBinding Padding}">
                        <ContentPresenter/>
                    </Border>
                    <ControlTemplate.Triggers>
                        <Trigger Property="IsMouseOver" Value="True">
                            <Setter TargetName="Border" Property="Background" Value="#1E293B"/>
                            <Setter TargetName="Border" Property="BorderBrush" Value="#475569"/>
                            <Setter Property="Foreground" Value="#38BDF8"/>
                        </Trigger>
                        <Trigger Property="IsSelected" Value="True">
                            <Setter TargetName="Border" Property="Background" Value="#2563EB"/>
                            <Setter TargetName="Border" Property="BorderBrush" Value="#60A5FA"/>
                            <Setter Property="Foreground" Value="#FFFFFF"/>
                            <Setter Property="FontWeight" Value="SemiBold"/>
                        </Trigger>
                        <Trigger Property="IsEnabled" Value="False">
                            <Setter Property="Foreground" Value="#64748B"/>
                        </Trigger>
                    </ControlTemplate.Triggers>
                </ControlTemplate>
            </Setter.Value>
        </Setter>
    </Style>

    <!-- LISTBOX STYLE -->
    <Style TargetType="ListBox">
        <Setter Property="Background" Value="#0F172A"/>
        <Setter Property="Foreground" Value="#F8FAFC"/>
        <Setter Property="BorderBrush" Value="#334155"/>
        <Setter Property="BorderThickness" Value="1"/>
        <Setter Property="Padding" Value="4"/>
        <Setter Property="ScrollViewer.HorizontalScrollBarVisibility" Value="Disabled"/>
        <Setter Property="ScrollViewer.VerticalScrollBarVisibility" Value="Auto"/>
    </Style>

    <!-- TEXTBOX STYLE -->
    <Style TargetType="TextBox">
        <Setter Property="Background" Value="#0F172A"/>
        <Setter Property="Foreground" Value="#F8FAFC"/>
        <Setter Property="CaretBrush" Value="#38BDF8"/>
        <Setter Property="SelectionBrush" Value="#2563EB"/>
        <Setter Property="BorderBrush" Value="#334155"/>
        <Setter Property="BorderThickness" Value="1"/>
        <Setter Property="Padding" Value="8,6"/>
        <Setter Property="FontSize" Value="12"/>
        <Setter Property="Template">
            <Setter.Value>
                <ControlTemplate TargetType="TextBox">
                    <Border x:Name="border"
                            Background="{TemplateBinding Background}"
                            BorderBrush="{TemplateBinding BorderBrush}"
                            BorderThickness="{TemplateBinding BorderThickness}"
                            CornerRadius="6"
                            Padding="{TemplateBinding Padding}">
                        <ScrollViewer x:Name="PART_ContentHost" Focusable="False" HorizontalScrollBarVisibility="Hidden" VerticalScrollBarVisibility="Hidden"/>
                    </Border>
                    <ControlTemplate.Triggers>
                        <Trigger Property="IsMouseOver" Value="True">
                            <Setter TargetName="border" Property="BorderBrush" Value="#475569"/>
                        </Trigger>
                        <Trigger Property="IsKeyboardFocused" Value="True">
                            <Setter TargetName="border" Property="BorderBrush" Value="#38BDF8"/>
                        </Trigger>
                        <Trigger Property="IsEnabled" Value="False">
                            <Setter Property="Opacity" Value="0.45"/>
                        </Trigger>
                    </ControlTemplate.Triggers>
                </ControlTemplate>
            </Setter.Value>
        </Setter>
    </Style>

    <!-- PASSWORDBOX STYLE -->
    <Style TargetType="PasswordBox">
        <Setter Property="Background" Value="#0F172A"/>
        <Setter Property="Foreground" Value="#F8FAFC"/>
        <Setter Property="CaretBrush" Value="#38BDF8"/>
        <Setter Property="SelectionBrush" Value="#2563EB"/>
        <Setter Property="BorderBrush" Value="#334155"/>
        <Setter Property="BorderThickness" Value="1"/>
        <Setter Property="Padding" Value="8,6"/>
        <Setter Property="FontSize" Value="12"/>
        <Setter Property="Template">
            <Setter.Value>
                <ControlTemplate TargetType="PasswordBox">
                    <Border x:Name="border"
                            Background="{TemplateBinding Background}"
                            BorderBrush="{TemplateBinding BorderBrush}"
                            BorderThickness="{TemplateBinding BorderThickness}"
                            CornerRadius="6"
                            Padding="{TemplateBinding Padding}">
                        <ScrollViewer x:Name="PART_ContentHost" Focusable="False" HorizontalScrollBarVisibility="Hidden" VerticalScrollBarVisibility="Hidden"/>
                    </Border>
                    <ControlTemplate.Triggers>
                        <Trigger Property="IsMouseOver" Value="True">
                            <Setter TargetName="border" Property="BorderBrush" Value="#475569"/>
                        </Trigger>
                        <Trigger Property="IsKeyboardFocused" Value="True">
                            <Setter TargetName="border" Property="BorderBrush" Value="#38BDF8"/>
                        </Trigger>
                        <Trigger Property="IsEnabled" Value="False">
                            <Setter Property="Opacity" Value="0.45"/>
                        </Trigger>
                    </ControlTemplate.Triggers>
                </ControlTemplate>
            </Setter.Value>
        </Setter>
    </Style>

    <!-- BUTTON STYLE (AuraCode Dark-Cyan Gradient & Glow) -->
    <Style TargetType="Button">
        <Setter Property="ToolTipService.ShowOnDisabled" Value="True"/>
        <Setter Property="Background" Value="#101C30"/>
        <Setter Property="Foreground" Value="#F8FAFC"/>
        <Setter Property="BorderBrush" Value="#3300E5FF"/>
        <Setter Property="BorderThickness" Value="1"/>
        <Setter Property="Padding" Value="12,7"/>
        <Setter Property="Margin" Value="3,2"/>
        <Setter Property="FontSize" Value="12"/>
        <Setter Property="FontWeight" Value="SemiBold"/>
        <Setter Property="Cursor" Value="Hand"/>
        <Setter Property="Template">
            <Setter.Value>
                <ControlTemplate TargetType="Button">
                    <Border x:Name="b"
                            Background="{TemplateBinding Background}"
                            BorderBrush="{TemplateBinding BorderBrush}"
                            BorderThickness="{TemplateBinding BorderThickness}"
                            CornerRadius="6"
                            Padding="{TemplateBinding Padding}">
                        <ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center"/>
                    </Border>
                    <ControlTemplate.Triggers>
                        <Trigger Property="IsMouseOver" Value="True">
                            <Setter TargetName="b" Property="Background" Value="#172B4D"/>
                            <Setter TargetName="b" Property="BorderBrush" Value="#00E5FF"/>
                        </Trigger>
                        <Trigger Property="IsPressed" Value="True">
                            <Setter TargetName="b" Property="Background" Value="#0077B6"/>
                            <Setter TargetName="b" Property="BorderBrush" Value="#00E5FF"/>
                        </Trigger>
                        <Trigger Property="IsEnabled" Value="False">
                            <Setter Property="Opacity" Value="0.45"/>
                        </Trigger>
                    </ControlTemplate.Triggers>
                </ControlTemplate>
            </Setter.Value>
        </Setter>
    </Style>

    <!-- TAB ITEM STYLE (AuraCode Dark-Cyan) -->
    <Style TargetType="TabItem">
        <Setter Property="SnapsToDevicePixels" Value="True"/>
        <Setter Property="Foreground" Value="#94A3B8"/>
        <Setter Property="Background" Value="#0B1324"/>
        <Setter Property="BorderBrush" Value="#3300E5FF"/>
        <Setter Property="BorderThickness" Value="1,1,1,0"/>
        <Setter Property="Padding" Value="14,8"/>
        <Setter Property="FontSize" Value="12"/>
        <Setter Property="Cursor" Value="Hand"/>
        <Setter Property="Template">
            <Setter.Value>
                <ControlTemplate TargetType="TabItem">
                    <Border x:Name="TabBorder"
                            Background="{TemplateBinding Background}"
                            BorderBrush="{TemplateBinding BorderBrush}"
                            BorderThickness="{TemplateBinding BorderThickness}"
                            CornerRadius="6,6,0,0"
                            Margin="3,0,3,0"
                            Padding="{TemplateBinding Padding}">
                        <ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center"/>
                    </Border>
                    <ControlTemplate.Triggers>
                        <Trigger Property="IsMouseOver" Value="True">
                            <Setter TargetName="TabBorder" Property="Background" Value="#142036"/>
                            <Setter Property="Foreground" Value="#00E5FF"/>
                        </Trigger>
                        <Trigger Property="IsSelected" Value="True">
                            <Setter TargetName="TabBorder" Property="Background" Value="#0077B6"/>
                            <Setter Property="Foreground" Value="#FFFFFF"/>
                            <Setter Property="FontWeight" Value="Bold"/>
                            <Setter TargetName="TabBorder" Property="BorderBrush" Value="#00E5FF"/>
                        </Trigger>
                    </ControlTemplate.Triggers>
                </ControlTemplate>
            </Setter.Value>
        </Setter>
    </Style>

    <!-- TAB CONTROL STYLE -->
    <Style TargetType="TabControl">
        <Setter Property="Background" Value="#18202C"/>
        <Setter Property="BorderBrush" Value="#334155"/>
        <Setter Property="BorderThickness" Value="1"/>
        <Setter Property="Foreground" Value="#F8FAFC"/>
        <Setter Property="Template">
            <Setter.Value>
                <ControlTemplate TargetType="TabControl">
                    <Grid>
                        <Grid.RowDefinitions>
                            <RowDefinition Height="Auto"/>
                            <RowDefinition Height="*"/>
                        </Grid.RowDefinitions>
                        <TabPanel Grid.Row="0"
                                  IsItemsHost="True"
                                  Background="Transparent"
                                  Margin="2,2,2,0"/>
                        <Border Grid.Row="1"
                                Background="{TemplateBinding Background}"
                                BorderBrush="{TemplateBinding BorderBrush}"
                                BorderThickness="{TemplateBinding BorderThickness}"
                                CornerRadius="0,6,6,6">
                            <ContentPresenter ContentSource="SelectedContent"
                                              Margin="{TemplateBinding Padding}"/>
                        </Border>
                    </Grid>
                </ControlTemplate>
            </Setter.Value>
        </Setter>
    </Style>

    <!-- CHECKBOX STYLE -->
    <Style TargetType="CheckBox">
        <Setter Property="Foreground" Value="#F8FAFC"/>
        <Setter Property="FontSize" Value="12"/>
        <Setter Property="Cursor" Value="Hand"/>
        <Setter Property="Template">
            <Setter.Value>
                <ControlTemplate TargetType="CheckBox">
                    <Grid VerticalAlignment="Center">
                        <Grid.ColumnDefinitions>
                            <ColumnDefinition Width="20"/>
                            <ColumnDefinition Width="*"/>
                        </Grid.ColumnDefinitions>
                        <Border x:Name="Box"
                                Width="16" Height="16"
                                Background="#1E293B"
                                BorderBrush="#475569"
                                BorderThickness="1"
                                CornerRadius="4"
                                HorizontalAlignment="Center"
                                VerticalAlignment="Center">
                            <Path x:Name="Check"
                                  Data="M 3 8 L 7 12 L 13 4"
                                  Stroke="#38BDF8"
                                  StrokeThickness="2"
                                  Visibility="Collapsed"/>
                        </Border>
                        <ContentPresenter Grid.Column="1"
                                          Margin="6,0,0,0"
                                          VerticalAlignment="Center"/>
                    </Grid>
                    <ControlTemplate.Triggers>
                        <Trigger Property="IsChecked" Value="True">
                            <Setter TargetName="Check" Property="Visibility" Value="Visible"/>
                            <Setter TargetName="Box" Property="Background" Value="#0F172A"/>
                            <Setter TargetName="Box" Property="BorderBrush" Value="#38BDF8"/>
                        </Trigger>
                        <Trigger Property="IsMouseOver" Value="True">
                            <Setter TargetName="Box" Property="BorderBrush" Value="#38BDF8"/>
                        </Trigger>
                        <Trigger Property="IsEnabled" Value="False">
                            <Setter Property="Opacity" Value="0.45"/>
                        </Trigger>
                    </ControlTemplate.Triggers>
                </ControlTemplate>
            </Setter.Value>
        </Setter>
    </Style>

    <!-- RADIOBUTTON STYLE -->
    <Style TargetType="RadioButton">
        <Setter Property="Foreground" Value="#F8FAFC"/>
        <Setter Property="FontSize" Value="12"/>
        <Setter Property="Cursor" Value="Hand"/>
        <Setter Property="Template">
            <Setter.Value>
                <ControlTemplate TargetType="RadioButton">
                    <Grid VerticalAlignment="Center">
                        <Grid.ColumnDefinitions>
                            <ColumnDefinition Width="20"/>
                            <ColumnDefinition Width="*"/>
                        </Grid.ColumnDefinitions>
                        <Border x:Name="Circle"
                                Width="16" Height="16"
                                Background="#1E293B"
                                BorderBrush="#475569"
                                BorderThickness="1"
                                CornerRadius="8"
                                HorizontalAlignment="Center"
                                VerticalAlignment="Center">
                            <Ellipse x:Name="Dot"
                                     Width="8" Height="8"
                                     Fill="#38BDF8"
                                     Visibility="Collapsed"/>
                        </Border>
                        <ContentPresenter Grid.Column="1"
                                          Margin="6,0,0,0"
                                          VerticalAlignment="Center"/>
                    </Grid>
                    <ControlTemplate.Triggers>
                        <Trigger Property="IsChecked" Value="True">
                            <Setter TargetName="Dot" Property="Visibility" Value="Visible"/>
                            <Setter TargetName="Circle" Property="Background" Value="#0F172A"/>
                            <Setter TargetName="Circle" Property="BorderBrush" Value="#38BDF8"/>
                        </Trigger>
                        <Trigger Property="IsMouseOver" Value="True">
                            <Setter TargetName="Circle" Property="BorderBrush" Value="#38BDF8"/>
                        </Trigger>
                        <Trigger Property="IsEnabled" Value="False">
                            <Setter Property="Opacity" Value="0.45"/>
                        </Trigger>
                    </ControlTemplate.Triggers>
                </ControlTemplate>
            </Setter.Value>
        </Setter>
    </Style>

    <!-- CONTEXTMENU STYLE -->
    <Style TargetType="ContextMenu">
        <Setter Property="Background" Value="#0F172A"/>
        <Setter Property="Foreground" Value="#F8FAFC"/>
        <Setter Property="BorderBrush" Value="#334155"/>
        <Setter Property="BorderThickness" Value="1"/>
        <Setter Property="Padding" Value="4"/>
    </Style>

    <!-- MENUITEM STYLE -->
    <Style TargetType="MenuItem">
        <Setter Property="Background" Value="Transparent"/>
        <Setter Property="Foreground" Value="#F8FAFC"/>
        <Setter Property="Padding" Value="8,5"/>
        <Setter Property="FontSize" Value="12"/>
        <Setter Property="Cursor" Value="Hand"/>
    </Style>

</ResourceDictionary>
""";

    private static ResourceDictionary? _cachedDictionary;

    public static ResourceDictionary GetDictionary()
    {
        return _cachedDictionary ??= (ResourceDictionary)XamlReader.Parse(DarkStylesXaml);
    }

    /// <summary>
    /// Applies the dark theme resources cleanly to any Window.
    /// </summary>
    public static void Apply(Window window)
    {
        window.Background = BackgroundBrush;
        window.Foreground = TextBrush;
        window.FontFamily = new FontFamily("Segoe UI");

        var dict = GetDictionary();
        foreach (object key in dict.Keys)
        {
            window.Resources[key] = dict[key];
        }
    }

    /// <summary>
    /// Applies the dark theme resources to a FrameworkElement / Panel.
    /// </summary>
    public static void ApplyToElement(FrameworkElement element)
    {
        var dict = GetDictionary();
        foreach (object key in dict.Keys)
        {
            element.Resources[key] = dict[key];
        }
    }
}

