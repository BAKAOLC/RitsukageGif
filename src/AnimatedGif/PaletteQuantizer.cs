﻿#region License Information (GPL v3)

/*
   Source code provocatively stolen from ShareX: https://github.com/ShareX/ShareX.
   (Seriously, awesome work over there, I used some of the parts to create an easy
   to use .NET package for everyone.)
   Their License:

   ShareX - A program that allows you to take screenshots and share any file type
   Copyright (c) 2007-2017 ShareX Team
   This program is free software; you can redistribute it and/or
   modify it under the terms of the GNU General Public License
   as published by the Free Software Foundation; either version 2
   of the License, or (at your option) any later version.
   This program is distributed in the hope that it will be useful,
   but WITHOUT ANY WARRANTY; without even the implied warranty of
   MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
   GNU General Public License for more details.
   You should have received a copy of the GNU General Public License
   along with this program; if not, write to the Free Software
   Foundation, Inc., 51 Franklin Street, Fifth Floor, Boston, MA  02110-1301, USA.
   Optionally you can also view the license at <http://www.gnu.org/licenses/>.
*/

#endregion License Information (GPL v3)


using System.Collections;
using System.Drawing;
using System.Drawing.Imaging;

namespace AnimatedGif
{
    /// <summary>
    ///     Summary description for PaletteQuantizer.
    /// </summary>
    public class PaletteQuantizer : Quantizer
    {
        /// <summary>
        ///     Lookup table for colors
        /// </summary>
        private readonly Hashtable _colorMap;

        /// <summary>
        ///     List of all colors in the palette
        /// </summary>
        protected Color[] Colors;

        /// <summary>
        ///     Construct the palette quantizer
        /// </summary>
        /// <param name="palette">The color palette to quantize to</param>
        /// <remarks>
        ///     Palette quantization only requires a single quantization step
        /// </remarks>
        public PaletteQuantizer(ArrayList palette)
            : base(true)
        {
            _colorMap = new Hashtable();

            Colors = new Color[palette.Count];
            palette.CopyTo(Colors);
        }

        /// <summary>
        ///     Override this to process the pixel in the second pass of the algorithm
        /// </summary>
        /// <param name="pixel">The pixel to quantize</param>
        /// <returns>The quantized value</returns>
        protected override byte QuantizePixel(Color32 pixel)
        {
            byte colorIndex = 0;
            var colorHash = pixel.ARGB;

            // Check if the color is in the lookup table
            if (_colorMap.ContainsKey(colorHash))
            {
                colorIndex = (byte)_colorMap[colorHash];
            }
            else
            {
                // Not found - loop through the palette and find the nearest match.
                // Firstly check the alpha value - if 0, lookup the transparent color
                if (0 == pixel.Alpha)
                {
                    // Transparent. Lookup the first color with an alpha value of 0
                    for (var index = 0; index < Colors.Length; index++)
                        if (0 == Colors[index].A)
                        {
                            colorIndex = (byte)index;
                            break;
                        }
                }
                else
                {
                    // Not transparent...
                    var leastDistance = int.MaxValue;
                    int red = pixel.Red;
                    int green = pixel.Green;
                    int blue = pixel.Blue;

                    // Loop through the entire palette, looking for the closest color match
                    for (var index = 0; index < Colors.Length; index++)
                    {
                        var paletteColor = Colors[index];

                        var redDistance = paletteColor.R - red;
                        var greenDistance = paletteColor.G - green;
                        var blueDistance = paletteColor.B - blue;

                        var distance = redDistance * redDistance +
                                       greenDistance * greenDistance +
                                       blueDistance * blueDistance;

                        if (distance < leastDistance)
                        {
                            colorIndex = (byte)index;
                            leastDistance = distance;

                            // And if it's an exact match, exit the loop
                            if (0 == distance)
                                break;
                        }
                    }
                }

                // Now I have the color, pop it into the hashtable for next time
                _colorMap.Add(colorHash, colorIndex);
            }

            return colorIndex;
        }

        /// <summary>
        ///     Retrieve the palette for the quantized image
        /// </summary>
        /// <param name="palette">Any old palette, this is overrwritten</param>
        /// <returns>The new color palette</returns>
        protected override ColorPalette GetPalette(ColorPalette palette)
        {
            for (var index = 0; index < Colors.Length; index++)
                palette.Entries[index] = Colors[index];

            return palette;
        }
    }
}