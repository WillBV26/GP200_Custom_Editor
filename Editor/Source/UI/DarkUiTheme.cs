using System.Windows;
using System.Windows.Controls;
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
    // Official AuraCode WPF Brushes
    public static readonly SolidColorBrush AuraBgDeepBrush = new(Color.FromRgb(6, 10, 18));        // #060A12 (Fondo principal de ventana)
    public static readonly SolidColorBrush AuraBgPanelBrush = new(Color.FromArgb(235, 13, 21, 38)); // #0D1526 @ 0.92 opacity (Tarjetas y paneles)
    public static readonly SolidColorBrush AuraBgCardBrush = new(Color.FromRgb(15, 25, 44));        // #0F192C (Tarjetas modulares)
    public static readonly SolidColorBrush AuraBgInputBrush = new(Color.FromRgb(9, 15, 28));        // #090F1C (Fondo de inputs)
    public static readonly SolidColorBrush AuraCyanPrimaryBrush = new(Color.FromRgb(0, 229, 255));  // #00E5FF (Bordes activos, brillo)
    public static readonly SolidColorBrush AuraCyanSecondaryBrush = new(Color.FromRgb(0, 150, 199)); // #0096C7 (Gradientes activos)
    public static readonly SolidColorBrush AuraBorderSubtleBrush = new(Color.FromRgb(30, 58, 95));  // #1E3A5F (Borde normal)
    public static readonly SolidColorBrush AuraTextMainBrush = new(Color.FromRgb(248, 250, 252));    // #F8FAFC (Blanco nítido)
    public static readonly SolidColorBrush AuraTextMutedBrush = new(Color.FromRgb(148, 163, 184));   // #94A3B8 (Gris azulado)
    public static readonly SolidColorBrush AuraHighlightBrush = new(Color.FromRgb(0, 119, 182));    // #0077B6 (Selección)

    // Backward-compatible brush aliases
    public static SolidColorBrush BackgroundBrush => AuraBgDeepBrush;
    public static SolidColorBrush PanelBrush => AuraBgPanelBrush;
    public static SolidColorBrush CardBrush => AuraBgCardBrush;
    public static SolidColorBrush BorderBrush => AuraBorderSubtleBrush;
    public static SolidColorBrush InputBrush => AuraBgInputBrush;
    public static SolidColorBrush InputBorderBrush => AuraBorderSubtleBrush;
    public static SolidColorBrush TextBrush => AuraTextMainBrush;
    public static SolidColorBrush MutedBrush => AuraTextMutedBrush;
    public static SolidColorBrush CyanPrimaryBrush => AuraCyanPrimaryBrush;
    public static SolidColorBrush CyanSecondaryBrush => AuraCyanSecondaryBrush;
    public static SolidColorBrush AccentBrush => AuraCyanPrimaryBrush;
    public static SolidColorBrush HighlightBrush => AuraHighlightBrush;
    public static readonly SolidColorBrush SuccessBrush = new(Color.FromRgb(16, 185, 129));      // #10B981
    public static readonly SolidColorBrush WarningBrush = new(Color.FromRgb(245, 158, 11));      // #F59E0B
    public static readonly SolidColorBrush DangerBrush = new(Color.FromRgb(239, 68, 68));         // #EF4444

    public static SolidColorBrush Brush(string hex) => new((Color)ColorConverter.ConvertFromString(hex));

    public const string DarkStylesXaml = """
<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">

    <!-- AuraCode Named Brushes -->
    <SolidColorBrush x:Key="AuraBgDeepBrush" Color="#060A12"/>
    <SolidColorBrush x:Key="AuraBgPanelBrush" Color="#EB0D1526"/>
    <SolidColorBrush x:Key="AuraBgCardBrush" Color="#0F192C"/>
    <SolidColorBrush x:Key="AuraBgInputBrush" Color="#090F1C"/>
    <SolidColorBrush x:Key="AuraCyanPrimaryBrush" Color="#00E5FF"/>
    <SolidColorBrush x:Key="AuraCyanSecondaryBrush" Color="#0096C7"/>
    <SolidColorBrush x:Key="AuraBorderSubtleBrush" Color="#1E3A5F"/>
    <SolidColorBrush x:Key="AuraTextMainBrush" Color="#F8FAFC"/>
    <SolidColorBrush x:Key="AuraTextMutedBrush" Color="#94A3B8"/>
    <SolidColorBrush x:Key="AuraHighlightBrush" Color="#0077B6"/>

    <!-- Explicit WPF SystemColors Override (Eliminates white-on-white / unstyled system popups) -->
    <SolidColorBrush x:Key="{x:Static SystemColors.WindowBrushKey}" Color="#090F1C"/>
    <SolidColorBrush x:Key="{x:Static SystemColors.WindowTextBrushKey}" Color="#F8FAFC"/>
    <SolidColorBrush x:Key="{x:Static SystemColors.HighlightBrushKey}" Color="#0077B6"/>
    <SolidColorBrush x:Key="{x:Static SystemColors.HighlightTextBrushKey}" Color="#FFFFFF"/>
    <SolidColorBrush x:Key="{x:Static SystemColors.ControlBrushKey}" Color="#0D1526"/>
    <SolidColorBrush x:Key="{x:Static SystemColors.ControlTextBrushKey}" Color="#F8FAFC"/>
    <SolidColorBrush x:Key="{x:Static SystemColors.InactiveSelectionHighlightBrushKey}" Color="#1E293B"/>
    <SolidColorBrush x:Key="{x:Static SystemColors.GrayTextBrushKey}" Color="#94A3B8"/>

    <!-- COMBOBOX TOGGLE BUTTON TEMPLATE -->
    <ControlTemplate x:Key="AuraComboBoxToggleButton" TargetType="ToggleButton">
        <Border x:Name="TemplateRoot"
                Background="#090F1C"
                BorderBrush="#1E3A5F"
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
                <Setter TargetName="TemplateRoot" Property="Background" Value="#101C30"/>
                <Setter TargetName="TemplateRoot" Property="BorderBrush" Value="#00E5FF"/>
                <Setter TargetName="Arrow" Property="Fill" Value="#38BDF8"/>
            </Trigger>
            <Trigger Property="IsChecked" Value="True">
                <Setter TargetName="TemplateRoot" Property="BorderBrush" Value="#00E5FF"/>
                <Setter TargetName="TemplateRoot" Property="Background" Value="#142238"/>
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
        <Setter Property="Background" Value="#090F1C"/>
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
        <Setter Property="Background" Value="#090F1C"/>
        <Setter Property="BorderBrush" Value="#1E3A5F"/>
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
                                    Background="#090F1C"
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

    <!-- BUTTON STYLE (AuraCode Dark-Cyan Gradient, Glow & Mandatory Spacing) -->
    <Style TargetType="Button">
        <Setter Property="ToolTipService.ShowOnDisabled" Value="True"/>
        <Setter Property="Foreground" Value="#F8FAFC"/>
        <Setter Property="BorderBrush" Value="#1E3A5F"/>
        <Setter Property="BorderThickness" Value="1"/>
        <Setter Property="Padding" Value="14,7"/>
        <Setter Property="Margin" Value="5,3,5,3"/>
        <Setter Property="FontSize" Value="12"/>
        <Setter Property="FontWeight" Value="SemiBold"/>
        <Setter Property="Cursor" Value="Hand"/>
        <Setter Property="Template">
            <Setter.Value>
                <ControlTemplate TargetType="Button">
                    <Border x:Name="b"
                            BorderBrush="{TemplateBinding BorderBrush}"
                            BorderThickness="{TemplateBinding BorderThickness}"
                            CornerRadius="6"
                            Padding="{TemplateBinding Padding}">
                        <Border.Background>
                            <LinearGradientBrush StartPoint="0,0" EndPoint="0,1">
                                <GradientStop Color="#142238" Offset="0.0"/>
                                <GradientStop Color="#0B1322" Offset="1.0"/>
                            </LinearGradientBrush>
                        </Border.Background>
                        <ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center"/>
                    </Border>
                    <ControlTemplate.Triggers>
                        <Trigger Property="IsMouseOver" Value="True">
                            <Setter TargetName="b" Property="BorderBrush" Value="#00E5FF"/>
                            <Setter TargetName="b" Property="Background">
                                <Setter.Value>
                                    <SolidColorBrush Color="#1A2F4C"/>
                                </Setter.Value>
                            </Setter>
                            <Setter TargetName="b" Property="Effect">
                                <Setter.Value>
                                    <DropShadowEffect Color="#00E5FF" BlurRadius="10" ShadowDepth="0" Opacity="0.4"/>
                                </Setter.Value>
                            </Setter>
                        </Trigger>
                        <Trigger Property="IsPressed" Value="True">
                            <Setter TargetName="b" Property="Background">
                                <Setter.Value>
                                    <LinearGradientBrush StartPoint="0,0" EndPoint="0,1">
                                        <GradientStop Color="#00B4D8" Offset="0.0"/>
                                        <GradientStop Color="#0077B6" Offset="1.0"/>
                                    </LinearGradientBrush>
                                </Setter.Value>
                            </Setter>
                            <Setter TargetName="b" Property="BorderBrush" Value="#00E5FF"/>
                            <Setter Property="Foreground" Value="#FFFFFF"/>
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
        <Setter Property="BorderBrush" Value="#1E3A5F"/>
        <Setter Property="BorderThickness" Value="1"/>
        <Setter Property="Padding" Value="14,7"/>
        <Setter Property="Margin" Value="5,3,5,3"/>
        <Setter Property="FontSize" Value="12"/>
        <Setter Property="FontWeight" Value="SemiBold"/>
        <Setter Property="Cursor" Value="Hand"/>
        <Setter Property="Template">
            <Setter.Value>
                <ControlTemplate TargetType="ToggleButton">
                    <Border x:Name="tb"
                            BorderBrush="{TemplateBinding BorderBrush}"
                            BorderThickness="{TemplateBinding BorderThickness}"
                            CornerRadius="6"
                            Padding="{TemplateBinding Padding}">
                        <Border.Background>
                            <LinearGradientBrush StartPoint="0,0" EndPoint="0,1">
                                <GradientStop Color="#142238" Offset="0.0"/>
                                <GradientStop Color="#0B1322" Offset="1.0"/>
                            </LinearGradientBrush>
                        </Border.Background>
                        <ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center"/>
                    </Border>
                    <ControlTemplate.Triggers>
                        <Trigger Property="IsMouseOver" Value="True">
                            <Setter TargetName="tb" Property="BorderBrush" Value="#00E5FF"/>
                            <Setter TargetName="tb" Property="Background">
                                <Setter.Value>
                                    <SolidColorBrush Color="#1A2F4C"/>
                                </Setter.Value>
                            </Setter>
                            <Setter TargetName="tb" Property="Effect">
                                <Setter.Value>
                                    <DropShadowEffect Color="#00E5FF" BlurRadius="10" ShadowDepth="0" Opacity="0.4"/>
                                </Setter.Value>
                            </Setter>
                        </Trigger>
                        <Trigger Property="IsChecked" Value="True">
                            <Setter TargetName="tb" Property="Background">
                                <Setter.Value>
                                    <LinearGradientBrush StartPoint="0,0" EndPoint="0,1">
                                        <GradientStop Color="#00B4D8" Offset="0.0"/>
                                        <GradientStop Color="#0077B6" Offset="1.0"/>
                                    </LinearGradientBrush>
                                </Setter.Value>
                            </Setter>
                            <Setter TargetName="tb" Property="BorderBrush" Value="#00E5FF"/>
                            <Setter Property="Foreground" Value="#FFFFFF"/>
                            <Setter Property="FontWeight" Value="Bold"/>
                            <Setter TargetName="tb" Property="Effect">
                                <Setter.Value>
                                    <DropShadowEffect Color="#00E5FF" BlurRadius="12" ShadowDepth="0" Opacity="0.5"/>
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

    <!-- TEXTBOX STYLE -->
    <Style TargetType="TextBox">
        <Setter Property="Background" Value="#090F1C"/>
        <Setter Property="Foreground" Value="#F8FAFC"/>
        <Setter Property="CaretBrush" Value="#00E5FF"/>
        <Setter Property="SelectionBrush" Value="#00B4D8"/>
        <Setter Property="BorderBrush" Value="#1E3A5F"/>
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
        <Setter Property="Background" Value="#090F1C"/>
        <Setter Property="Foreground" Value="#F8FAFC"/>
        <Setter Property="CaretBrush" Value="#00E5FF"/>
        <Setter Property="SelectionBrush" Value="#00B4D8"/>
        <Setter Property="BorderBrush" Value="#1E3A5F"/>
        <Setter Property="BorderThickness" Value="1"/>
        <Setter Property="Padding" Value="8,6"/>
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
        <Setter Property="Background" Value="#090F1C"/>
        <Setter Property="Foreground" Value="#F8FAFC"/>
        <Setter Property="CaretBrush" Value="#00E5FF"/>
        <Setter Property="SelectionBrush" Value="#00B4D8"/>
        <Setter Property="BorderBrush" Value="#1E3A5F"/>
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
                            <Setter TargetName="Border" Property="Background" Value="#142238"/>
                            <Setter TargetName="Border" Property="BorderBrush" Value="#1E3A5F"/>
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
        <Setter Property="Background" Value="#090F1C"/>
        <Setter Property="Foreground" Value="#F8FAFC"/>
        <Setter Property="BorderBrush" Value="#1E3A5F"/>
        <Setter Property="BorderThickness" Value="1"/>
        <Setter Property="Padding" Value="4"/>
        <Setter Property="ScrollViewer.HorizontalScrollBarVisibility" Value="Disabled"/>
        <Setter Property="ScrollViewer.VerticalScrollBarVisibility" Value="Auto"/>
    </Style>

    <!-- SLIDER STYLE (AuraCode Glowing Track & Thumb) -->
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
                                Background="#1E293B"
                                BorderBrush="#1E3A5F"
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

    <Style TargetType="ScrollBar">
        <Setter Property="SnapsToDevicePixels" Value="True"/>
        <Setter Property="OverridesDefaultStyle" Value="True"/>
        <Setter Property="Width" Value="8"/>
        <Setter Property="Background" Value="#060A12"/>
        <Setter Property="Template">
            <Setter.Value>
                <ControlTemplate TargetType="ScrollBar">
                    <Grid Background="{TemplateBinding Background}">
                        <Track x:Name="PART_Track" IsDirectionReversed="True">
                            <Track.Thumb>
                                <Thumb Style="{StaticResource AuraScrollBarThumb}"/>
                            </Track.Thumb>
                        </Track>
                    </Grid>
                </ControlTemplate>
            </Setter.Value>
        </Setter>
        <Style.Triggers>
            <Trigger Property="Orientation" Value="Horizontal">
                <Setter Property="Width" Value="Auto"/>
                <Setter Property="Height" Value="8"/>
            </Trigger>
        </Style.Triggers>
    </Style>

    <!-- TAB ITEM STYLE -->
    <Style TargetType="TabItem">
        <Setter Property="SnapsToDevicePixels" Value="True"/>
        <Setter Property="Foreground" Value="#94A3B8"/>
        <Setter Property="Background" Value="#0D1526"/>
        <Setter Property="BorderBrush" Value="#1E3A5F"/>
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
                            <Setter TargetName="TabBorder" Property="Background" Value="#142238"/>
                            <Setter Property="Foreground" Value="#00E5FF"/>
                        </Trigger>
                        <Trigger Property="IsSelected" Value="True">
                            <Setter TargetName="TabBorder" Property="Background">
                                <Setter.Value>
                                    <LinearGradientBrush StartPoint="0,0" EndPoint="0,1">
                                        <GradientStop Color="#00B4D8" Offset="0.0"/>
                                        <GradientStop Color="#0077B6" Offset="1.0"/>
                                    </LinearGradientBrush>
                                </Setter.Value>
                            </Setter>
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
        <Setter Property="Background" Value="#0D1526"/>
        <Setter Property="BorderBrush" Value="#1E3A5F"/>
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
                                Background="#090F1C"
                                BorderBrush="#1E3A5F"
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
                            <Setter TargetName="Box" Property="Background" Value="#0F192C"/>
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
                                Background="#090F1C"
                                BorderBrush="#1E3A5F"
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
                            <Setter TargetName="Circle" Property="Background" Value="#0F192C"/>
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
        <Setter Property="Background" Value="#090F1C"/>
        <Setter Property="BorderBrush" Value="#1E3A5F"/>
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
        <Setter Property="Background" Value="#090F1C"/>
        <Setter Property="Foreground" Value="#F8FAFC"/>
        <Setter Property="BorderBrush" Value="#00E5FF"/>
        <Setter Property="BorderThickness" Value="1"/>
        <Setter Property="Padding" Value="4"/>
        <Setter Property="Template">
            <Setter.Value>
                <ControlTemplate TargetType="ContextMenu">
                    <Border Background="#090F1C"
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
                                  Fill="#94A3B8"
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
                                <Border Background="#090F1C"
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
                            <Setter TargetName="ItemBorder" Property="Background" Value="#1E3A5F"/>
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
    /// Applies the dark theme resources cleanly to any Window.
    /// </summary>
    public static void Apply(Window window)
    {
        window.Background = AuraBgDeepBrush;
        window.Foreground = AuraTextMainBrush;
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
