Add-Type @'
using System; using System.Runtime.InteropServices;
public class Win32 {
  [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr hdc, uint f);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left,Top,Right,Bottom; }
}
'@
Add-Type -AssemblyName System.Drawing
$hwnd=[IntPtr]87559876; $rect=New-Object Win32+RECT
[Win32]::GetWindowRect($hwnd,[ref]$rect)|Out-Null
$w=$rect.Right-$rect.Left; $h=$rect.Bottom-$rect.Top
$bmp=New-Object System.Drawing.Bitmap($w,$h); $g=[System.Drawing.Graphics]::FromImage($bmp)
[Win32]::PrintWindow($hwnd,$g.GetHdc(),2)|Out-Null; $g.ReleaseHdc()
$bmp.Save("C:/Users/rvn/source/repos/ModbusForge/plc_fbd_px.png",[System.Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose()
Write-Output "saved $($w)x$($h)"
