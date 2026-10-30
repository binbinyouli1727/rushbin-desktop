using System;
using System.Windows.Media;
using System.Windows.Media.Imaging;
namespace DesktopBox {
public static class TrayArtwork {
 // Classify visible pixels, not the transparent RGB padding or dark parts of colour icons.
 public static BitmapSource Lighten(BitmapSource source,out bool changed){changed=false;if(source==null)return null;var bitmap=new FormatConvertedBitmap(source,PixelFormats.Bgra32,null,0);int stride=bitmap.PixelWidth*4;var pixels=new byte[stride*bitmap.PixelHeight];bitmap.CopyPixels(pixels,stride,0);long visible=0,dark=0;for(int n=0;n<pixels.Length;n+=4){int a=pixels[n+3];if(a<24)continue;visible+=a;int min=Math.Min(pixels[n],Math.Min(pixels[n+1],pixels[n+2])),max=Math.Max(pixels[n],Math.Max(pixels[n+1],pixels[n+2]));if(max<=72&&max-min<=18)dark+=a;}if(visible==0||dark<visible*.985)return source;for(int n=0;n<pixels.Length;n+=4){byte colour=pixels[n+3]==0?(byte)0:(byte)255;pixels[n]=pixels[n+1]=pixels[n+2]=colour;}var result=BitmapSource.Create(bitmap.PixelWidth,bitmap.PixelHeight,bitmap.DpiX,bitmap.DpiY,PixelFormats.Bgra32,null,pixels,stride);result.Freeze();changed=true;return result;}
}
}
