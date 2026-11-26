using System;
using System.Drawing;
using System.Drawing.Imaging;

namespace BIMRadar.ClashNavigator
{
    // #97: pure helpers to auto-crop an exported view image to its content (no Revit types).
    public static class SnapshotCrop
    {
        // Bounding box of pixels where R, G or B < threshold. Rectangle.Empty if there are none.
        public static Rectangle ContentRect(Bitmap src, int threshold = 245)
        {
            if (src == null || src.Width <= 0 || src.Height <= 0) return Rectangle.Empty;
            int w = src.Width, h = src.Height;
            int minX = w, minY = h, maxX = -1, maxY = -1;
            var data = src.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                int stride = data.Stride;
                var row = new byte[Math.Abs(stride)];
                for (int y = 0; y < h; y++)
                {
                    IntPtr p = new IntPtr(data.Scan0.ToInt64() + (long)y * stride);
                    System.Runtime.InteropServices.Marshal.Copy(p, row, 0, w * 4);
                    for (int x = 0; x < w; x++)
                    {
                        int i = x * 4; // B, G, R, A
                        if (row[i] < threshold || row[i + 1] < threshold || row[i + 2] < threshold)
                        {
                            if (x < minX) minX = x;
                            if (x > maxX) maxX = x;
                            if (y < minY) minY = y;
                            if (y > maxY) maxY = y;
                        }
                    }
                }
            }
            finally { src.UnlockBits(data); }
            if (maxX < 0) return Rectangle.Empty;
            return new Rectangle(minX, minY, maxX - minX + 1, maxY - minY + 1);
        }

        // Grow content by margin (of its larger side), expand to aspect (w/h), centre, keep inside image.
        public static Rectangle FitCrop(Rectangle content, Size image, double aspect, double margin = 0.12)
        {
            var whole = new Rectangle(0, 0, image.Width, image.Height);
            if (content.IsEmpty || aspect <= 0.0001 || image.Width <= 0 || image.Height <= 0) return whole;
            double m = Math.Max(content.Width, content.Height) * margin;
            double w = content.Width + 2 * m, h = content.Height + 2 * m;
            if (w / h < aspect) w = h * aspect; else h = w / aspect;
            if (w > image.Width || h > image.Height) return whole;
            double cx = content.X + content.Width / 2.0, cy = content.Y + content.Height / 2.0;
            double x = cx - w / 2.0, y = cy - h / 2.0;
            if (x < 0) x = 0;
            if (y < 0) y = 0;
            if (x + w > image.Width) x = image.Width - w;
            if (y + h > image.Height) y = image.Height - h;
            int ix = (int)Math.Floor(x), iy = (int)Math.Floor(y);
            int iw = Math.Min((int)Math.Ceiling(w), image.Width - ix);
            int ih = Math.Min((int)Math.Ceiling(h), image.Height - iy);
            return new Rectangle(ix, iy, iw, ih);
        }
    }
}
