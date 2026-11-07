using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace Gp200.Replica;

/// <summary>
/// Master AuraCode Dark-Cyan Glassmorphism Design System for GP-200 Community Editor.
/// Completely overrides native Windows 11 / Aero WPF control templates to prevent any white-on-white
/// or unstyled rendering, providing glowing cyan accents, glassmorphic panels, and high-contrast readability.
/// </summary>
public static class DarkUiTheme
{
    // Official AuraCode WPF Brushes (MASTER PROMPT DEFINITIVO - PILAR 1)
    public static readonly SolidColorBrush AuraBgDeepBrush = new(Color.FromRgb(7, 11, 18));          // #070B12 (Fondo Base Principal)
    public static readonly SolidColorBrush AuraBgSurfaceBrush = new(Color.FromRgb(12, 19, 32));       // #0C1320 (Paneles / Mixer / Playlist)
    public static readonly SolidColorBrush AuraBgPanelBrush = AuraBgSurfaceBrush;                     // Alias #0C1320
    public static readonly SolidColorBrush AuraBgCardBrush = new(Color.FromRgb(17, 28, 46));         // #111C2E (Tarjetas / Slots / Canales)
    public static readonly SolidColorBrush AuraBgInputBrush = new(Color.FromRgb(9, 14, 23));          // #090E17 (Fondo Inputs / ComboBox / TextBox)
    public static readonly SolidColorBrush AuraCyanPrimaryBrush = new(Color.FromRgb(0, 229, 255));    // #00E5FF (Acento Primario Cian Neón)
    public static readonly SolidColorBrush AuraCyanSolidBrush = new(Color.FromRgb(0, 180, 216));      // #00B4D8 (Acento Secundario / Botón Activo)
    public static readonly SolidColorBrush AuraCyanSecondaryBrush = AuraCyanSolidBrush;               // Alias #00B4D8
    public static readonly SolidColorBrush AuraBorderSubtleBrush = new(Color.FromRgb(26, 43, 69));    // #1A2B45 (Bordes de Paneles / Tarjetas)
    public static readonly SolidColorBrush AuraBorderBrush = AuraBorderSubtleBrush;                   // Alias #1A2B45
    public static readonly SolidColorBrush AuraTextMainBrush = new(Color.FromRgb(248, 250, 252));     // #F8FAFC (Texto Principal)
    public static readonly SolidColorBrush AuraTextMutedBrush = new(Color.FromRgb(139, 155, 180));    // #8B9BB4 (Texto Secundario)
    public static readonly SolidColorBrush AuraHighlightBrush = new(Color.FromRgb(0, 119, 182));      // #0077B6 (Selección)
    public static readonly SolidColorBrush AuraActiveTextBrush = new(Color.FromRgb(5, 8, 15));        // #05080F (Texto oscuro en botones activos)

    // Backward-compatible brush aliases
    public static SolidColorBrush BackgroundBrush => AuraBgDeepBrush;
    public static SolidColorBrush PanelBrush => AuraBgSurfaceBrush;
    public static SolidColorBrush CardBrush => AuraBgCardBrush;
    public static SolidColorBrush BorderBrush => AuraBorderSubtleBrush;
    public static SolidColorBrush InputBrush => AuraBgInputBrush;
    public static SolidColorBrush InputBorderBrush => AuraBorderSubtleBrush;
    public static SolidColorBrush TextBrush => AuraTextMainBrush;
    public static SolidColorBrush MutedBrush => AuraTextMutedBrush;
    public static SolidColorBrush CyanPrimaryBrush => AuraCyanPrimaryBrush;
    public static SolidColorBrush CyanSecondaryBrush => AuraCyanSolidBrush;
    public static SolidColorBrush AccentBrush => AuraCyanPrimaryBrush;
    public static SolidColorBrush HighlightBrush => AuraHighlightBrush;
    public static readonly SolidColorBrush SuccessBrush = new(Color.FromRgb(16, 185, 129));        // #10B981
    public static readonly SolidColorBrush WarningBrush = new(Color.FromRgb(245, 158, 11));        // #F59E0B
    public static readonly SolidColorBrush DangerBrush = new(Color.FromRgb(239, 68, 68));           // #EF4444
    public static SolidColorBrush ErrorBrush => DangerBrush;                                        // Alias #EF4444

    public static SolidColorBrush Brush(string hex) => new((Color)ColorConverter.ConvertFromString(hex));

    public const string DarkStylesXaml = """
<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">

    <!-- AuraCode Named Brushes (MASTER PROMPT DEFINITIVO - PILAR 1) -->
    <SolidColorBrush x:Key="AuraBgDeepBrush" Color="#070B12"/>
    <SolidColorBrush x:Key="AuraBgSurfaceBrush" Color="#0C1320"/>
    <SolidColorBrush x:Key="AuraBgPanelBrush" Color="#0C1320"/>
    <SolidColorBrush x:Key="AuraBgCardBrush" Color="#111C2E"/>
    <SolidColorBrush x:Key="AuraBgInputBrush" Color="#090E17"/>
    <SolidColorBrush x:Key="AuraCyanPrimaryBrush" Color="#00E5FF"/>
    <SolidColorBrush x:Key="AuraCyanSolidBrush" Color="#00B4D8"/>
    <SolidColorBrush x:Key="AuraCyanSecondaryBrush" Color="#00B4D8"/>
    <SolidColorBrush x:Key="AuraBorderSubtleBrush" Color="#1A2B45"/>
    <SolidColorBrush x:Key="AuraBorderBrush" Color="#1A2B45"/>
    <SolidColorBrush x:Key="AuraTextMainBrush" Color="#F8FAFC"/>
    <SolidColorBrush x:Key="AuraTextMutedBrush" Color="#8B9BB4"/>
    <SolidColorBrush x:Key="AuraHighlightBrush" Color="#0077B6"/>
    <SolidColorBrush x:Key="AuraActiveTextBrush" Color="#05080F"/>

    <!-- Explicit WPF SystemColors Override (Eliminates white-on-white / unstyled system popups) -->
    <SolidColorBrush x:Key="{x:Static SystemColors.WindowBrushKey}" Color="#070B12"/>
    <SolidColorBrush x:Key="{x:Static SystemColors.WindowTextBrushKey}" Color="#F8FAFC"/>
    <SolidColorBrush x:Key="{x:Static SystemColors.HighlightBrushKey}" Color="#0077B6"/>
    <SolidColorBrush x:Key="{x:Static SystemColors.HighlightTextBrushKey}" Color="#FFFFFF"/>
    <SolidColorBrush x:Key="{x:Static SystemColors.ControlBrushKey}" Color="#0C1320"/>
    <SolidColorBrush x:Key="{x:Static SystemColors.ControlTextBrushKey}" Color="#F8FAFC"/>
    <SolidColorBrush x:Key="{x:Static SystemColors.MenuBrushKey}" Color="#0C1320"/>
    <SolidColorBrush x:Key="{x:Static SystemColors.MenuTextBrushKey}" Color="#F8FAFC"/>
    <SolidColorBrush x:Key="{x:Static SystemColors.InactiveSelectionHighlightBrushKey}" Color="#111C2E"/>
    <SolidColorBrush x:Key="{x:Static SystemColors.GrayTextBrushKey}" Color="#8B9BB4"/>

    <!-- COMBOBOX TOGGLE BUTTON TEMPLATE -->
    <ControlTemplate x:Key="AuraComboBoxToggleButton" TargetType="ToggleButton">
        <Border x:Name="TemplateRoot"
                Background="#090E17"
                BorderBrush="#1A2B45"
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
                <Setter TargetName="TemplateRoot" Property="Background" Value="#162640"/>
                <Setter TargetName="TemplateRoot" Property="BorderBrush" Value="#00E5FF"/>
                <Setter TargetName="Arrow" Property="Fill" Value="#00E5FF"/>
            </Trigger>
            <Trigger Property="IsChecked" Value="True">
                <Setter TargetName="TemplateRoot" Property="BorderBrush" Value="#00E5FF"/>
                <Setter TargetName="TemplateRoot" Property="Background" Value="#162640"/>
            </Trigger>
            <Trigger Property="IsEnabled" Value="False">
                <Setter TargetName="TemplateRoot" Property="Opacity" Value="0.45"/>
            </Trigger>
        </ControlTemplate.Triggers>
    </ControlTemplate>

    <!-- COMBOBOX ITEM STYLE -->
    <Style TargetType="ComboBoxItem">
        <Setter Property="SnapsToDevicePixels" Value="True"/>
        <Setter Property="Padding" Value="10,6"/>
        <Setter Property="Background" Value="#0C1320"/>
        <Setter Property="Foreground" Value="#F8FAFC"/>
        <Setter Property="FontSize" Value="12"/>
        <Setter Property="HorizontalContentAlignment" Value="Stretch"/>
        <Setter Property="Cursor" Value="Hand"/>
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
                            <Setter TargetName="ItemBorder" Property="Background" Value="#0077B6"/>
                            <Setter Property="Foreground" Value="#FFFFFF"/>
                        </Trigger>
                        <Trigger Property="IsSelected" Value="True">
                            <Setter TargetName="ItemBorder" Property="Background" Value="#0096C7"/>
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

    <!-- FULL COMBOBOX CONTROL TEMPLATE -->
    <Style TargetType="ComboBox">
        <Setter Property="SnapsToDevicePixels" Value="True"/>
        <Setter Property="Foreground" Value="#F8FAFC"/>
        <Setter Property="Background" Value="#090E17"/>
        <Setter Property="BorderBrush" Value="#1A2B45"/>
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
                        <ToggleButton x:Name="ToggleButton"
                                      Template="{StaticResource AuraComboBoxToggleButton}"
                                      Focusable="False"
                                      ClickMode="Press"
                                      IsChecked="{Binding IsDropDownOpen, Mode=TwoWay, RelativeSource={RelativeSource TemplatedParent}}"/>
                        
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

                        <Popup x:Name="PART_Popup"
                               AllowsTransparency="True"
                               IsOpen="{TemplateBinding IsDropDownOpen}"
                               Placement="Bottom"
                               PopupAnimation="Slide"
                               Focusable="False">
                            <Border x:Name="DropDownBorder"
                                    Background="#0C1320"
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

    <!-- BUTTON STYLE (AuraCode Dark-Cyan Glass, Glow & Mandatory Spacing) -->
    <Style TargetType="Button">
        <Setter Property="ToolTipService.ShowOnDisabled" Value="True"/>
        <Setter Property="Foreground" Value="#F8FAFC"/>
        <Setter Property="BorderBrush" Value="#1A2B45"/>
        <Setter Property="BorderThickness" Value="1"/>
        <Setter Property="Padding" Value="14,7"/>
        <Setter Property="Margin" Value="5,3,5,3"/>
        <Setter Property="FontSize" Value="12"/>
        <Setter Property="FontWeight" Value="Medium"/>
        <Setter Property="Cursor" Value="Hand"/>
        <Setter Property="Template">
            <Setter.Value>
                <ControlTemplate TargetType="Button">
                    <Border x:Name="b"
                            Background="#111C2E"
                            BorderBrush="#1A2B45"
                            BorderThickness="1"
                            CornerRadius="6"
                            Padding="{TemplateBinding Padding}">
                        <ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center"/>
                    </Border>
                    <ControlTemplate.Triggers>
                        <Trigger Property="IsMouseOver" Value="True">
                            <Setter TargetName="b" Property="Background" Value="#162640"/>
                            <Setter TargetName="b" Property="BorderBrush" Value="#00E5FF"/>
                            <Setter TargetName="b" Property="Effect">
                                <Setter.Value>
                                    <DropShadowEffect Color="#00E5FF" BlurRadius="8" ShadowDepth="0" Opacity="0.4"/>
                                </Setter.Value>
                            </Setter>
                        </Trigger>
                        <Trigger Property="IsPressed" Value="True">
                            <Setter TargetName="b" Property="Background" Value="#00E5FF"/>
                            <Setter TargetName="b" Property="BorderBrush" Value="#00E5FF"/>
                            <Setter Property="Foreground" Value="#05080F"/>
                            <Setter Property="FontWeight" Value="Bold"/>
                        </Trigger>
                        <Trigger Property="Tag" Value="Active">
                            <Setter TargetName="b" Property="Background" Value="#00E5FF"/>
                            <Setter TargetName="b" Property="BorderBrush" Value="#00E5FF"/>
                            <Setter Property="Foreground" Value="#05080F"/>
                            <Setter Property="FontWeight" Value="Bold"/>
                            <Setter TargetName="b" Property="Effect">
                                <Setter.Value>
                                    <DropShadowEffect Color="#00E5FF" BlurRadius="10" ShadowDepth="0" Opacity="0.5"/>
                                </Setter.Value>
                            </Setter>
                        </Trigger>
                        <Trigger Property="IsEnabled" Value="False">
                            <Setter Property="Opacity" Value="0.45"/>
                        </Trigger>
                    </ControlTemplate.Triggers>
                </ControlTemplate>
            </Setter.Value>
        </Setter>
    </Style>

    <!-- TOGGLEBUTTON STYLE -->
    <Style TargetType="ToggleButton">
        <Setter Property="ToolTipService.ShowOnDisabled" Value="True"/>
        <Setter Property="Foreground" Value="#F8FAFC"/>
        <Setter Property="BorderBrush" Value="#1A2B45"/>
        <Setter Property="BorderThickness" Value="1"/>
        <Setter Property="Padding" Value="14,7"/>
        <Setter Property="Margin" Value="5,3,5,3"/>
        <Setter Property="FontSize" Value="12"/>
        <Setter Property="FontWeight" Value="Medium"/>
        <Setter Property="Cursor" Value="Hand"/>
        <Setter Property="Template">
            <Setter.Value>
                <ControlTemplate TargetType="ToggleButton">
                    <Border x:Name="tb"
                            Background="#111C2E"
                            BorderBrush="#1A2B45"
                            BorderThickness="1"
                            CornerRadius="6"
                            Padding="{TemplateBinding Padding}">
                        <ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center"/>
                    </Border>
                    <ControlTemplate.Triggers>
                        <Trigger Property="IsMouseOver" Value="True">
                            <Setter TargetName="tb" Property="Background" Value="#162640"/>
                            <Setter TargetName="tb" Property="BorderBrush" Value="#00E5FF"/>
                            <Setter TargetName="tb" Property="Effect">
                                <Setter.Value>
                                    <DropShadowEffect Color="#00E5FF" BlurRadius="8" ShadowDepth="0" Opacity="0.4"/>
                                </Setter.Value>
                            </Setter>
                        </Trigger>
                        <Trigger Property="IsChecked" Value="True">
                            <Setter TargetName="tb" Property="Background" Value="#00E5FF"/>
                            <Setter TargetName="tb" Property="BorderBrush" Value="#00E5FF"/>
                            <Setter Property="Foreground" Value="#05080F"/>
                            <Setter Property="FontWeight" Value="Bold"/>
                            <Setter TargetName="tb" Property="Effect">
                                <Setter.Value>
                                    <DropShadowEffect Color="#00E5FF" BlurRadius="12" ShadowDepth="0" Opacity="0.5"/>
                                </Setter.Value>
                            </Setter>
                        </Trigger>
                        <Trigger Property="Tag" Value="Active">
                            <Setter TargetName="tb" Property="Background" Value="#00E5FF"/>
                            <Setter TargetName="tb" Property="BorderBrush" Value="#00E5FF"/>
                            <Setter Property="Foreground" Value="#05080F"/>
                            <Setter Property="FontWeight" Value="Bold"/>
                        </Trigger>
                        <Trigger Property="IsEnabled" Value="False">
                            <Setter Property="Opacity" Value="0.45"/>
                        </Trigger>
                    </ControlTemplate.Triggers>
                </ControlTemplate>
            </Setter.Value>
        </Setter>
    </Style>

    <!-- TEXTBOX STYLE -->
    <Style TargetType="TextBox">
        <Setter Property="Background" Value="#090E17"/>
        <Setter Property="Foreground" Value="#F8FAFC"/>
        <Setter Property="CaretBrush" Value="#00E5FF"/>
        <Setter Property="SelectionBrush" Value="#00B4D8"/>
        <Setter Property="BorderBrush" Value="#1A2B45"/>
        <Setter Property="BorderThickness" Value="1"/>
        <Setter Property="Padding" Value="8,5"/>
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
                            <Setter TargetName="border" Property="BorderBrush" Value="#0096C7"/>
                        </Trigger>
                        <Trigger Property="IsKeyboardFocused" Value="True">
                            <Setter TargetName="border" Property="BorderBrush" Value="#00E5FF"/>
                            <Setter TargetName="border" Property="Effect">
                                <Setter.Value>
                                    <DropShadowEffect Color="#00E5FF" BlurRadius="8" ShadowDepth="0" Opacity="0.35"/>
                                </Setter.Value>
                            </Setter>
                        </Trigger>
                        <Trigger Property="IsEnabled" Value="False">
                            <Setter Property="Opacity" Value="0.45"/>
                        </Trigger>
                    </ControlTemplate.Triggers>
                </ControlTemplate>
            </Setter.Value>
        </Setter>
    </Style>

    <!-- RICHTEXTBOX STYLE -->
    <Style TargetType="RichTextBox">
        <Setter Property="Background" Value="#090E17"/>
        <Setter Property="Foreground" Value="#F8FAFC"/>
        <Setter Property="CaretBrush" Value="#00E5FF"/>
        <Setter Property="SelectionBrush" Value="#00B4D8"/>
        <Setter Property="BorderBrush" Value="#1A2B45"/>
        <Setter Property="BorderThickness" Value="1"/>
        <Setter Property="Padding" Value="8,5"/>
        <Setter Property="FontSize" Value="12"/>
        <Setter Property="Template">
            <Setter.Value>
                <ControlTemplate TargetType="RichTextBox">
                    <Border x:Name="border"
                            Background="{TemplateBinding Background}"
                            BorderBrush="{TemplateBinding BorderBrush}"
                            BorderThickness="{TemplateBinding BorderThickness}"
                            CornerRadius="6"
                            Padding="{TemplateBinding Padding}">
                        <ScrollViewer x:Name="PART_ContentHost" Focusable="False"/>
                    </Border>
                    <ControlTemplate.Triggers>
                        <Trigger Property="IsKeyboardFocused" Value="True">
                            <Setter TargetName="border" Property="BorderBrush" Value="#00E5FF"/>
                        </Trigger>
                    </ControlTemplate.Triggers>
                </ControlTemplate>
            </Setter.Value>
        </Setter>
    </Style>

    <!-- PASSWORDBOX STYLE -->
    <Style TargetType="PasswordBox">
        <Setter Property="Background" Value="#090E17"/>
        <Setter Property="Foreground" Value="#F8FAFC"/>
        <Setter Property="CaretBrush" Value="#00E5FF"/>
        <Setter Property="SelectionBrush" Value="#00B4D8"/>
        <Setter Property="BorderBrush" Value="#1A2B45"/>
        <Setter Property="BorderThickness" Value="1"/>
        <Setter Property="Padding" Value="8,5"/>
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
                            <Setter TargetName="border" Property="BorderBrush" Value="#0096C7"/>
                        </Trigger>
                        <Trigger Property="IsKeyboardFocused" Value="True">
                            <Setter TargetName="border" Property="BorderBrush" Value="#00E5FF"/>
                        </Trigger>
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
        <Setter Property="Foreground" Value="#F8FAFC"/>
        <Setter Property="Padding" Value="9,6"/>
        <Setter Property="Margin" Value="1,1"/>
        <Setter Property="FontSize" Value="12"/>
        <Setter Property="HorizontalContentAlignment" Value="Stretch"/>
        <Setter Property="Cursor" Value="Hand"/>
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
                            <Setter TargetName="Border" Property="Background" Value="#111C2E"/>
                            <Setter TargetName="Border" Property="BorderBrush" Value="#1A2B45"/>
                            <Setter Property="Foreground" Value="#00E5FF"/>
                        </Trigger>
                        <Trigger Property="IsSelected" Value="True">
                            <Setter TargetName="Border" Property="Background" Value="#0077B6"/>
                            <Setter TargetName="Border" Property="BorderBrush" Value="#00E5FF"/>
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
        <Setter Property="Background" Value="#090E17"/>
        <Setter Property="Foreground" Value="#F8FAFC"/>
        <Setter Property="BorderBrush" Value="#1A2B45"/>
        <Setter Property="BorderThickness" Value="1"/>
        <Setter Property="Padding" Value="4"/>
        <Setter Property="ScrollViewer.HorizontalScrollBarVisibility" Value="Disabled"/>
        <Setter Property="ScrollViewer.VerticalScrollBarVisibility" Value="Auto"/>
    </Style>

    <!-- VERTICAL SLIDER TEMPLATE (DAW Mixer Console Fader) -->
    <ControlTemplate x:Key="AuraVerticalSliderTemplate" TargetType="Slider">
        <Grid HorizontalAlignment="Center">
            <Border x:Name="Track"
                    Width="6"
                    Background="#090E17"
                    BorderBrush="#1A2B45"
                    BorderThickness="1"
                    CornerRadius="3"/>
            <Track x:Name="PART_Track" IsDirectionReversed="True">
                <Track.DecreaseRepeatButton>
                    <RepeatButton Command="{x:Static Slider.DecreaseLarge}">
                        <RepeatButton.Template>
                            <ControlTemplate TargetType="RepeatButton">
                                <Border Width="6" Background="#00E5FF" CornerRadius="0,0,3,3"/>
                            </ControlTemplate>
                        </RepeatButton.Template>
                    </RepeatButton>
                </Track.DecreaseRepeatButton>
                <Track.Thumb>
                    <Thumb Width="20" Height="12" Cursor="Hand">
                        <Thumb.Template>
                            <ControlTemplate TargetType="Thumb">
                                <Border Background="#00E5FF" BorderBrush="#FFFFFF" BorderThickness="1" CornerRadius="3">
                                    <Border.Effect>
                                        <DropShadowEffect Color="#00E5FF" BlurRadius="8" ShadowDepth="0" Opacity="0.6"/>
                                    </Border.Effect>
                                    <Border Width="12" Height="2" Background="#05080F" VerticalAlignment="Center" HorizontalAlignment="Center"/>
                                </Border>
                            </ControlTemplate>
                        </Thumb.Template>
                    </Thumb>
                </Track.Thumb>
            </Track>
        </Grid>
    </ControlTemplate>

    <!-- SLIDER STYLE (AuraCode Glowing Track & Thumb - Horizontal & Vertical Console Fader) -->
    <Style TargetType="Slider">
        <Setter Property="SnapsToDevicePixels" Value="True"/>
        <Setter Property="OverridesDefaultStyle" Value="True"/>
        <Setter Property="Cursor" Value="Hand"/>
        <Setter Property="Template">
            <Setter.Value>
                <ControlTemplate TargetType="Slider">
                    <Grid VerticalAlignment="Center">
                        <Border x:Name="Track"
                                Height="6"
                                Background="#090E17"
                                BorderBrush="#1A2B45"
                                BorderThickness="1"
                                CornerRadius="3"/>
                        <Track x:Name="PART_Track">
                            <Track.DecreaseRepeatButton>
                                <RepeatButton Command="{x:Static Slider.DecreaseLarge}">
                                    <RepeatButton.Template>
                                        <ControlTemplate TargetType="RepeatButton">
                                            <Border Height="6" Background="#00E5FF" CornerRadius="3,0,0,3"/>
                                        </ControlTemplate>
                                    </RepeatButton.Template>
                                </RepeatButton>
                            </Track.DecreaseRepeatButton>
                            <Track.Thumb>
                                <Thumb Width="16" Height="16">
                                    <Thumb.Template>
                                        <ControlTemplate TargetType="Thumb">
                                            <Ellipse Fill="#00E5FF" Stroke="#FFFFFF" StrokeThickness="1.5">
                                                <Ellipse.Effect>
                                                    <DropShadowEffect Color="#00E5FF" BlurRadius="8" ShadowDepth="0" Opacity="0.6"/>
                                                </Ellipse.Effect>
                                            </Ellipse>
                                        </ControlTemplate>
                                    </Thumb.Template>
                                </Thumb>
                            </Track.Thumb>
                        </Track>
                    </Grid>
                </ControlTemplate>
            </Setter.Value>
        </Setter>
        <Style.Triggers>
            <Trigger Property="Orientation" Value="Vertical">
                <Setter Property="Template" Value="{StaticResource AuraVerticalSliderTemplate}"/>
            </Trigger>
        </Style.Triggers>
    </Style>

    <!-- SCROLLBAR THUMB & STYLE (Slim 8px AuraCode) -->
    <Style x:Key="AuraScrollBarThumb" TargetType="Thumb">
        <Setter Property="SnapsToDevicePixels" Value="True"/>
        <Setter Property="OverridesDefaultStyle" Value="True"/>
        <Setter Property="Template">
            <Setter.Value>
                <ControlTemplate TargetType="Thumb">
                    <Border Background="#1E3A5F" CornerRadius="4" Margin="1">
                        <VisualStateManager.VisualStateGroups>
                            <VisualStateGroup x:Name="CommonStates">
                                <VisualState x:Name="Normal"/>
                                <VisualState x:Name="MouseOver">
                                    <Storyboard>
                                        <ColorAnimation Storyboard.TargetProperty="(Border.Background).(SolidColorBrush.Color)"
                                                        To="#00E5FF" Duration="0:0:0.15"/>
                                    </Storyboard>
                                </VisualState>
                            </VisualStateGroup>
                        </VisualStateManager.VisualStateGroups>
                    </Border>
                </ControlTemplate>
            </Setter.Value>
        </Setter>
    </Style>

    <!-- Vertical ScrollBar Template (IsDirectionReversed="True" for standard 0 at Top, Max at Bottom) -->
    <ControlTemplate x:Key="AuraVerticalScrollBarTemplate" TargetType="ScrollBar">
        <Grid Background="{TemplateBinding Background}">
            <Track x:Name="PART_Track" IsDirectionReversed="True">
                <Track.Thumb>
                    <Thumb Style="{StaticResource AuraScrollBarThumb}"/>
                </Track.Thumb>
            </Track>
        </Grid>
    </ControlTemplate>

    <!-- Horizontal ScrollBar Template (IsDirectionReversed="False" for standard 0 at Left, Max at Right) -->
    <ControlTemplate x:Key="AuraHorizontalScrollBarTemplate" TargetType="ScrollBar">
        <Grid Background="{TemplateBinding Background}">
            <Track x:Name="PART_Track" IsDirectionReversed="False">
                <Track.Thumb>
                    <Thumb Style="{StaticResource AuraScrollBarThumb}"/>
                </Track.Thumb>
            </Track>
        </Grid>
    </ControlTemplate>

    <Style TargetType="ScrollBar">
        <Setter Property="SnapsToDevicePixels" Value="True"/>
        <Setter Property="OverridesDefaultStyle" Value="True"/>
        <Setter Property="Width" Value="8"/>
        <Setter Property="Background" Value="#060A12"/>
        <Setter Property="Template" Value="{StaticResource AuraVerticalScrollBarTemplate}"/>
        <Style.Triggers>
            <Trigger Property="Orientation" Value="Horizontal">
                <Setter Property="Width" Value="Auto"/>
                <Setter Property="Height" Value="8"/>
                <Setter Property="Template" Value="{StaticResource AuraHorizontalScrollBarTemplate}"/>
            </Trigger>
        </Style.Triggers>
    </Style>

    <!-- TAB ITEM STYLE -->
    <Style TargetType="TabItem">
        <Setter Property="SnapsToDevicePixels" Value="True"/>
        <Setter Property="Foreground" Value="#8B9BB4"/>
        <Setter Property="Background" Value="#0C1320"/>
        <Setter Property="BorderBrush" Value="#1A2B45"/>
        <Setter Property="BorderThickness" Value="1,1,1,0"/>
        <Setter Property="Padding" Value="16,8"/>
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
                            <Setter TargetName="TabBorder" Property="Background" Value="#162640"/>
                            <Setter Property="Foreground" Value="#00E5FF"/>
                        </Trigger>
                        <Trigger Property="IsSelected" Value="True">
                            <Setter TargetName="TabBorder" Property="Background" Value="#00E5FF"/>
                            <Setter Property="Foreground" Value="#05080F"/>
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
        <Setter Property="Background" Value="#0C1320"/>
        <Setter Property="BorderBrush" Value="#1A2B45"/>
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
                                Background="#090E17"
                                BorderBrush="#1A2B45"
                                BorderThickness="1"
                                CornerRadius="4"
                                HorizontalAlignment="Center"
                                VerticalAlignment="Center">
                            <Path x:Name="Check"
                                  Data="M 3 8 L 7 12 L 13 4"
                                  Stroke="#00E5FF"
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
                            <Setter TargetName="Box" Property="Background" Value="#111C2E"/>
                            <Setter TargetName="Box" Property="BorderBrush" Value="#00E5FF"/>
                            <Setter TargetName="Box" Property="Effect">
                                <Setter.Value>
                                    <DropShadowEffect Color="#00E5FF" BlurRadius="6" ShadowDepth="0" Opacity="0.5"/>
                                </Setter.Value>
                            </Setter>
                        </Trigger>
                        <Trigger Property="IsMouseOver" Value="True">
                            <Setter TargetName="Box" Property="BorderBrush" Value="#00E5FF"/>
                        </Trigger>
                        <Trigger Property="IsEnabled" Value="False">
                            <Setter Property="Opacity" Value="0.45"/>
                        </Trigger>
                    </ControlTemplate.Triggers>
                </ControlTemplate>
            </Setter.Value>
        </Setter>
    </Style>

    <!-- CHAINTOGGLE STYLE (AuraCode Pill Switch Style for CheckBox) -->
    <Style x:Key="ChainToggle" TargetType="CheckBox">
        <Setter Property="ToolTipService.ShowOnDisabled" Value="True"/>
        <Setter Property="Foreground" Value="#F8FAFC"/>
        <Setter Property="FontSize" Value="11"/>
        <Setter Property="Cursor" Value="Hand"/>
        <Setter Property="Template">
            <Setter.Value>
                <ControlTemplate TargetType="CheckBox">
                    <StackPanel Orientation="Horizontal" VerticalAlignment="Center">
                        <Border x:Name="switchBorder"
                                Width="32" Height="16"
                                CornerRadius="8"
                                Background="#111C2E"
                                BorderBrush="#1A2B45"
                                BorderThickness="1">
                            <Ellipse x:Name="dot"
                                     Width="10" Height="10"
                                     Margin="2,0,2,0"
                                     HorizontalAlignment="Left"
                                     VerticalAlignment="Center"
                                     Fill="#8B9BB4"/>
                        </Border>
                        <ContentPresenter Margin="6,0,0,0" VerticalAlignment="Center"/>
                    </StackPanel>
                    <ControlTemplate.Triggers>
                        <Trigger Property="IsChecked" Value="True">
                            <Setter TargetName="switchBorder" Property="Background" Value="#00E5FF"/>
                            <Setter TargetName="switchBorder" Property="BorderBrush" Value="#00E5FF"/>
                            <Setter TargetName="switchBorder" Property="Effect">
                                <Setter.Value>
                                    <DropShadowEffect Color="#00E5FF" BlurRadius="8" ShadowDepth="0" Opacity="0.5"/>
                                </Setter.Value>
                            </Setter>
                            <Setter TargetName="dot" Property="HorizontalAlignment" Value="Right"/>
                            <Setter TargetName="dot" Property="Fill" Value="#05080F"/>
                            <Setter Property="Foreground" Value="#00E5FF"/>
                            <Setter Property="FontWeight" Value="SemiBold"/>
                        </Trigger>
                        <Trigger Property="IsMouseOver" Value="True">
                            <Setter TargetName="switchBorder" Property="BorderBrush" Value="#00E5FF"/>
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
                                Background="#090E17"
                                BorderBrush="#1A2B45"
                                BorderThickness="1"
                                CornerRadius="8"
                                HorizontalAlignment="Center"
                                VerticalAlignment="Center">
                            <Ellipse x:Name="Dot"
                                     Width="8" Height="8"
                                     Fill="#00E5FF"
                                     Visibility="Collapsed"/>
                        </Border>
                        <ContentPresenter Grid.Column="1"
                                          Margin="6,0,0,0"
                                          VerticalAlignment="Center"/>
                    </Grid>
                    <ControlTemplate.Triggers>
                        <Trigger Property="IsChecked" Value="True">
                            <Setter TargetName="Dot" Property="Visibility" Value="Visible"/>
                            <Setter TargetName="Circle" Property="Background" Value="#111C2E"/>
                            <Setter TargetName="Circle" Property="BorderBrush" Value="#00E5FF"/>
                        </Trigger>
                        <Trigger Property="IsMouseOver" Value="True">
                            <Setter TargetName="Circle" Property="BorderBrush" Value="#00E5FF"/>
                        </Trigger>
                        <Trigger Property="IsEnabled" Value="False">
                            <Setter Property="Opacity" Value="0.45"/>
                        </Trigger>
                    </ControlTemplate.Triggers>
                </ControlTemplate>
            </Setter.Value>
        </Setter>
    </Style>

    <!-- PROGRESSBAR STYLE -->
    <Style TargetType="ProgressBar">
        <Setter Property="Height" Value="8"/>
        <Setter Property="Background" Value="#090E17"/>
        <Setter Property="BorderBrush" Value="#1A2B45"/>
        <Setter Property="BorderThickness" Value="1"/>
        <Setter Property="Template">
            <Setter.Value>
                <ControlTemplate TargetType="ProgressBar">
                    <Grid x:Name="TemplateRoot" SnapsToDevicePixels="True">
                        <Border CornerRadius="4"
                                Background="{TemplateBinding Background}"
                                BorderBrush="{TemplateBinding BorderBrush}"
                                BorderThickness="{TemplateBinding BorderThickness}"/>
                        <Border x:Name="PART_Track" Margin="1"/>
                        <Border x:Name="PART_Indicator"
                                HorizontalAlignment="Left"
                                Margin="1"
                                CornerRadius="3"
                                Background="#00E5FF">
                            <Border.Effect>
                                <DropShadowEffect Color="#00E5FF" BlurRadius="6" ShadowDepth="0" Opacity="0.5"/>
                            </Border.Effect>
                        </Border>
                    </Grid>
                </ControlTemplate>
            </Setter.Value>
        </Setter>
    </Style>

    <!-- CONTEXTMENU STYLE -->
    <Style TargetType="ContextMenu">
        <Setter Property="Background" Value="#090E17"/>
        <Setter Property="Foreground" Value="#F8FAFC"/>
        <Setter Property="BorderBrush" Value="#00E5FF"/>
        <Setter Property="BorderThickness" Value="1"/>
        <Setter Property="Padding" Value="4"/>
        <Setter Property="Template">
            <Setter.Value>
                <ControlTemplate TargetType="ContextMenu">
                    <Border Background="#090E17"
                            BorderBrush="#00E5FF"
                            BorderThickness="1"
                            CornerRadius="8"
                            Padding="4">
                        <Border.Effect>
                            <DropShadowEffect Color="#00E5FF" BlurRadius="12" ShadowDepth="0" Opacity="0.3"/>
                        </Border.Effect>
                        <StackPanel IsItemsHost="True" KeyboardNavigation.DirectionalNavigation="Cycle"/>
                    </Border>
                </ControlTemplate>
            </Setter.Value>
        </Setter>
    </Style>

    <!-- MENUITEM STYLE -->
    <Style TargetType="MenuItem">
        <Setter Property="Background" Value="Transparent"/>
        <Setter Property="Foreground" Value="#F8FAFC"/>
        <Setter Property="Padding" Value="10,6"/>
        <Setter Property="FontSize" Value="12"/>
        <Setter Property="Cursor" Value="Hand"/>
        <Setter Property="Template">
            <Setter.Value>
                <ControlTemplate TargetType="MenuItem">
                    <Border x:Name="ItemBorder"
                            Background="{TemplateBinding Background}"
                            BorderBrush="Transparent"
                            BorderThickness="1"
                            CornerRadius="5"
                            Margin="2,1,2,1"
                            Padding="{TemplateBinding Padding}">
                        <Grid>
                            <Grid.ColumnDefinitions>
                                <ColumnDefinition Width="22"/>
                                <ColumnDefinition Width="*"/>
                                <ColumnDefinition Width="22"/>
                            </Grid.ColumnDefinitions>

                            <!-- Checkmark / Icon -->
                            <TextBlock x:Name="CheckMark" Grid.Column="0" Text="✓"
                                       Foreground="#00E5FF" FontWeight="Bold"
                                       VerticalAlignment="Center" HorizontalAlignment="Center"
                                       Visibility="Collapsed"/>

                            <!-- Header content -->
                            <ContentPresenter Grid.Column="1"
                                              ContentSource="Header"
                                              RecognizesAccessKey="True"
                                              VerticalAlignment="Center"
                                              Margin="4,0,8,0"/>

                            <!-- Arrow for submenu -->
                            <Path x:Name="SubmenuArrow" Grid.Column="2"
                                  Data="M 0,0 L 4,4 L 0,8 Z"
                                  Fill="#8B9BB4"
                                  VerticalAlignment="Center"
                                  HorizontalAlignment="Center"
                                  Visibility="Collapsed"/>

                            <!-- Submenu Popup -->
                            <Popup x:Name="PART_Popup"
                                   IsOpen="{Binding IsSubmenuOpen, RelativeSource={RelativeSource TemplatedParent}}"
                                   Placement="Right"
                                   AllowsTransparency="True"
                                   Focusable="False"
                                   PopupAnimation="Fade">
                                <Border Background="#090E17"
                                        BorderBrush="#00E5FF"
                                        BorderThickness="1"
                                        CornerRadius="8"
                                        Padding="4"
                                        Margin="4">
                                    <Border.Effect>
                                        <DropShadowEffect Color="#00E5FF" BlurRadius="12" ShadowDepth="0" Opacity="0.35"/>
                                    </Border.Effect>
                                    <ScrollViewer MaxHeight="450"
                                                  VerticalScrollBarVisibility="Auto"
                                                  HorizontalScrollBarVisibility="Disabled">
                                        <ItemsPresenter KeyboardNavigation.DirectionalNavigation="Cycle"/>
                                    </ScrollViewer>
                                </Border>
                            </Popup>
                        </Grid>
                    </Border>
                    <ControlTemplate.Triggers>
                        <Trigger Property="HasItems" Value="True">
                            <Setter TargetName="SubmenuArrow" Property="Visibility" Value="Visible"/>
                        </Trigger>
                        <Trigger Property="IsHighlighted" Value="True">
                            <Setter TargetName="ItemBorder" Property="Background" Value="#0077B6"/>
                            <Setter TargetName="ItemBorder" Property="BorderBrush" Value="#00E5FF"/>
                            <Setter Property="Foreground" Value="#FFFFFF"/>
                            <Setter TargetName="SubmenuArrow" Property="Fill" Value="#FFFFFF"/>
                        </Trigger>
                        <Trigger Property="IsSubmenuOpen" Value="True">
                            <Setter TargetName="ItemBorder" Property="Background" Value="#1A2B45"/>
                            <Setter TargetName="ItemBorder" Property="BorderBrush" Value="#00E5FF"/>
                            <Setter Property="Foreground" Value="#00E5FF"/>
                        </Trigger>
                        <Trigger Property="IsChecked" Value="True">
                            <Setter TargetName="CheckMark" Property="Visibility" Value="Visible"/>
                        </Trigger>
                        <Trigger Property="IsEnabled" Value="False">
                            <Setter Property="Foreground" Value="#4B5563"/>
                            <Setter TargetName="SubmenuArrow" Property="Fill" Value="#4B5563"/>
                        </Trigger>
                    </ControlTemplate.Triggers>
                </ControlTemplate>
            </Setter.Value>
        </Setter>
    </Style>

</ResourceDictionary>
""";

    private static ResourceDictionary? _cachedDictionary;

    public static ResourceDictionary GetDictionary()
    {
        return _cachedDictionary ??= (ResourceDictionary)XamlReader.Parse(DarkStylesXaml);
    }

    /// <summary>
    /// Injects AuraCode resources and ControlTemplates into Application.Current globally.
    /// This ensures all native WPF controls and windows inherit AuraCode theme automatically.
    /// </summary>
    public static void ApplyGlobalAuraCodeTheme()
    {
        try
        {
            var dict = GetDictionary();
            var app = Application.Current;
            if (app != null)
            {
                if (!app.Resources.MergedDictionaries.Contains(dict))
                {
                    app.Resources.MergedDictionaries.Add(dict);
                }
                foreach (object key in dict.Keys)
                {
                    app.Resources[key] = dict[key];
                }
            }
        }
        catch
        {
            // Fallback gracefully if Application is not yet initialized (e.g. unit tests)
        }
    }

    /// <summary>
    /// Applies the dark theme resources cleanly to any Window, enabling resizing with grip
    /// and hooking visual tree enforcement to eliminate any legacy light backgrounds.
    /// </summary>
    public static void Apply(Window window)
    {
        window.Background = AuraBgDeepBrush;
        window.Foreground = AuraTextMainBrush;
        window.FontFamily = new FontFamily("Segoe UI");
        window.ResizeMode = ResizeMode.CanResizeWithGrip;

        var dict = GetDictionary();
        if (!window.Resources.MergedDictionaries.Contains(dict))
        {
            window.Resources.MergedDictionaries.Add(dict);
        }
        foreach (object key in dict.Keys)
        {
            window.Resources[key] = dict[key];
        }

        window.Loaded += (_, _) => EnforceAuraCodeOnVisualTree(window);
    }

    /// <summary>
    /// Applies the dark theme resources to a FrameworkElement / Panel.
    /// </summary>
    public static void ApplyToElement(FrameworkElement element)
    {
        var dict = GetDictionary();
        if (!element.Resources.MergedDictionaries.Contains(dict))
        {
            element.Resources.MergedDictionaries.Add(dict);
        }
        foreach (object key in dict.Keys)
        {
            element.Resources[key] = dict[key];
        }

        element.Loaded += (_, _) => EnforceAuraCodeOnVisualTree(element);
    }

    /// <summary>
    /// Traverses the visual and logical tree iteratively to ensure no light backgrounds,
    /// white containers, or unstyled system colors remain visible anywhere in the UI.
    /// Uses a visited set and a breadth-first queue to guarantee O(N) single-pass execution without UI thread freezing.
    /// </summary>
    public static void EnforceAuraCodeOnVisualTree(DependencyObject? root)
    {
        if (root == null) return;

        var visited = new HashSet<DependencyObject>();
        var queue = new Queue<DependencyObject>();
        queue.Enqueue(root);
        visited.Add(root);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            try
            {
                EnforceElementStyle(current);

                // Traverse visual tree
                int visualCount = VisualTreeHelper.GetChildrenCount(current);
                for (int i = 0; i < visualCount; i++)
                {
                    var child = VisualTreeHelper.GetChild(current, i);
                    if (child != null && visited.Add(child))
                    {
                        queue.Enqueue(child);
                    }
                }

                // Traverse logical tree
                foreach (object child in LogicalTreeHelper.GetChildren(current))
                {
                    if (child is DependencyObject depChild && visited.Add(depChild))
                    {
                        queue.Enqueue(depChild);
                    }
                }
            }
            catch
            {
                // Ignore any individual elements that do not support tree traversal
            }
        }
    }

    private static void EnforceElementStyle(DependencyObject d)
    {
        if (d is Window win)
        {
            win.Background = AuraBgDeepBrush;
            win.Foreground = AuraTextMainBrush;
        }
        else if (d is Border border)
        {
            if (IsLightOrSystemBrush(border.Background))
                border.Background = AuraBgSurfaceBrush;
            if (IsLightOrWhiteBrush(border.BorderBrush))
                border.BorderBrush = AuraBorderSubtleBrush;
        }
        else if (d is Panel panel)
        {
            if (IsLightOrSystemBrush(panel.Background))
                panel.Background = AuraBgSurfaceBrush;
        }
        else if (d is TextBlock tb)
        {
            if (IsDarkOrBlackBrush(tb.Foreground))
                tb.Foreground = AuraTextMainBrush;
        }
        else if (d is TextBox or PasswordBox)
        {
            if (d is Control ctrl)
            {
                if (IsLightOrSystemBrush(ctrl.Background))
                    ctrl.Background = AuraBgInputBrush;
                if (IsDarkOrBlackBrush(ctrl.Foreground))
                    ctrl.Foreground = AuraTextMainBrush;
            }
        }
        else if (d is Button or ToggleButton)
        {
            if (d is Control btn)
            {
                btn.Cursor = System.Windows.Input.Cursors.Hand;
                if (IsLightOrSystemBrush(btn.Background))
                    btn.Background = AuraBgCardBrush;
            }
        }
        else if (d is ComboBox cb)
        {
            cb.Cursor = System.Windows.Input.Cursors.Hand;
            if (IsLightOrSystemBrush(cb.Background))
                cb.Background = AuraBgInputBrush;
        }
        else if (d is Control ctrl)
        {
            if (IsLightOrSystemBrush(ctrl.Background))
                ctrl.Background = AuraBgCardBrush;
            if (IsDarkOrBlackBrush(ctrl.Foreground))
                ctrl.Foreground = AuraTextMainBrush;
        }
    }

    private static bool IsLightOrSystemBrush(Brush? brush)
    {
        if (brush == null) return false;
        if (brush == Brushes.White || brush == Brushes.LightGray || brush == Brushes.Gainsboro ||
            brush == Brushes.GhostWhite || brush == Brushes.Snow || brush == Brushes.WhiteSmoke ||
            brush == Brushes.AntiqueWhite || brush == Brushes.Ivory || brush == Brushes.FloralWhite ||
            brush == SystemColors.WindowBrush || brush == SystemColors.ControlBrush)
            return true;

        if (brush is SolidColorBrush scb)
        {
            Color c = scb.Color;
            return c.R > 200 && c.G > 200 && c.B > 200 && c.A > 20;
        }
        return false;
    }

    private static bool IsLightOrWhiteBrush(Brush? brush)
    {
        if (brush == null) return false;
        if (brush == Brushes.White || brush == Brushes.LightGray || brush == Brushes.Gainsboro) return true;
        if (brush is SolidColorBrush scb)
        {
            Color c = scb.Color;
            return c.R > 210 && c.G > 210 && c.B > 210;
        }
        return false;
    }

    private static bool IsDarkOrBlackBrush(Brush? brush)
    {
        if (brush == null) return false;
        if (brush == Brushes.Black || brush == SystemColors.WindowTextBrush || brush == SystemColors.ControlTextBrush) return true;
        if (brush is SolidColorBrush scb)
        {
            Color c = scb.Color;
            return c.R < 30 && c.G < 30 && c.B < 30 && c.A > 200;
        }
        return false;
    }
}
