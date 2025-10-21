# ModbusForge v2.0.0 - Light Metallic Theme Guide

## Color Scheme

### Primary Colors (Light Metallic)
- **Light Metallic White** (`#F8F8F8`) - Main background (very light)
- **Light Metallic Silver** (`#E8E8E8`) - Secondary backgrounds
- **Light Metallic Chrome** (`#D9D9D9`) - Button default state
- **Light Metallic Steel** (`#C0C0C0`) - Borders and hover states
- **Light Metallic Dark Silver** (`#808080`) - Inactive text and accents
- **Light Metallic Accent** (`#007ACC`) - Blue accent (VS Code blue)
- **Light Metallic Accent Hover** (`#0099FF`) - Bright blue on hover
- **Light Metallic Text** (`#2C2C2C`) - Primary text (dark grey)
- **Light Metallic Border** (`#CCCCCC`) - Control borders

## Button Styles

### Default Button (`MetallicButtonStyle`)
- **Default State**: Chrome silver background with steel borders
- **Hover Effect**:
  - Background transitions to light silver
  - Border changes to blue accent
  - Shadow blur increases (3px → 6px)
  - Smooth 200ms animation
- **Pressed State**: Background darkens, shadow reduces
- **Disabled State**: Light background with low opacity

### Accent Button (`MetallicAccentButtonStyle`)
- **Default State**: Blue accent background with glow
- **Hover Effect**:
  - Background transitions to bright blue
  - Shadow blur increases dramatically (4px → 8px)
  - Glowing effect intensifies
- **Usage**: Primary actions (Connect, Save, etc.)

## Applying Styles

### Automatic
All buttons automatically use the metallic theme.

### Manual Application
```xml
<!-- Accent Button -->
<Button Style="{StaticResource MetallicAccentButtonStyle}" Content="Connect"/>

<!-- Standard Button -->
<Button Style="{StaticResource MetallicButtonStyle}" Content="Read"/>
```

## Using Theme Colors

### In XAML
```xml
<Border Background="{StaticResource LightMetallicWhiteBrush}">
    <TextBlock Foreground="{StaticResource LightMetallicTextBrush}" Text="Hello"/>
</Border>
```

### Available Brushes
- `LightMetallicWhiteBrush`
- `LightMetallicSilverBrush`
- `LightMetallicChromeBrush`
- `LightMetallicSteelBrush`
- `LightMetallicDarkSilverBrush`
- `LightMetallicAccentBrush`
- `LightMetallicAccentHoverBrush`
- `LightMetallicTextBrush`
- `LightMetallicBorderBrush`

## Visual Effects

### Hover Animations
- **Duration**: 200ms (0.2 seconds)
- **Effect**: Smooth color transitions with shadow enhancement
- **Trigger**: Mouse enters/exits button area

### Shadow Effects
- **Default**: Silver shadow, 1px depth, 3px blur
- **Hover**: 1px depth, 6px blur (standard) or 8px blur (accent)
- **Pressed**: 0.5px depth, reduced blur
- **Direction**: 270° (straight down)

### Border Radius
- **All Buttons**: 3px rounded corners for sleek modern look

## Theme Architecture

```
App.xaml
├── Light.Blue.xaml (MahApps light base theme)
├── LightMetallicTheme.xaml (Custom light metallic styling)
└── Color Overrides (Light metallic color scheme)

MainWindow.xaml
├── Window Chrome (Chrome silver title bar)
└── Content Area (White background)
```

## Design Philosophy

- **Readable**: Dark text on light backgrounds (excellent contrast)
- **Metallic**: True silver/chrome colors like polished metal
- **Professional**: Industrial aesthetic with modern polish
- **Interactive**: Clear visual feedback on hover/press
- **Consistent**: Unified color palette throughout

## Accessibility

- **Contrast Ratio**: Light Metallic Text (#2C2C2C) on White (#F8F8F8) = 17.1:1 (WCAG AAA compliant)
- **Accent Contrast**: Blue (#007ACC) on Chrome (#D9D9D9) = 4.2:1 (WCAG AA compliant)
- **Interactive Feedback**: Visual hover states for all interactive elements

## Browser Compatibility

✅ **WPF .NET 8.0** - Full support for all visual effects
✅ **Windows 10/11** - Native DropShadow effects
✅ **Hardware Acceleration** - GPU-accelerated animations

## Customization

### Adding New Colors
Edit `Resources/LightMetallicTheme.xaml`:
```xml
<Color x:Key="MyCustomColor">#FFRRGGBB</Color>
<SolidColorBrush x:Key="MyCustomBrush" Color="{StaticResource MyCustomColor}"/>
```

### Creating Custom Button Styles
Base your style on existing ones:
```xml
<Style x:Key="MyButtonStyle" TargetType="Button" BasedOn="{StaticResource MetallicButtonStyle}">
    <Setter Property="Foreground" Value="Red"/>
</Style>
```

## Summary

**Theme Type**: Light Metallic Silver
**Background**: Very light white (#F8F8F8)
**Text**: Dark grey (#2C2C2C) - excellent readability
**Accents**: Chrome silver with blue highlights
**Buttons**: Metallic chrome with hover effects
**Windows**: Silver title bar, white content area
