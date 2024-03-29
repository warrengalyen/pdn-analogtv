﻿/*
 * Implementation of the PAL format
 * 
 * Dependency: MathUtil.cs
 * 
 * 2023-2024 Warren Galyen
 *
 * Permission is hereby granted, free of charge, to any person
 * obtaining a copy of this software and associated documentation
 * files (the "Software"), to deal in the Software without
 * restriction, including without limitation the rights to use,
 * copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the
 * Software is furnished to do so, subject to the following
 * conditions:
 *
 * The above copyright notice and this permission notice shall be
 * included in all copies or substantial portions of the Software.
 *
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
 * EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES
 * OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
 * NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT
 * HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY,
 * WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR
 * OTHER DEALINGS IN THE SOFTWARE.
 */

namespace AnalogTVFilter
{
    // The PAL format, used in Europe, etc.
    public class PALFormat : AnalogFormat
    {
        const double PALGamma = 2.8;

        public PALFormat() : base(0.299, // R to Y
                                  0.587, // G to Y
                                  0.114, // B to Y
                                  0.436, // U maximum
                                  0.615, // V maximum
                                  0.0, //  Chroma conversion phase relative to YUV (zero in this case because PAL uses YUV exactly)
                                  5e+6, // Main bandwidth
                                  0.75e+6, // Side bandwidth
                                  1.3e+6, // Color bandwidth lower part
                                  0.57e+6, // Color bandwidth upper part
                                  4433618.75, // Color subcarrier frequency
                                  625, // Total scanlines
                                  576, // Visible scanlines
                                  50.0, // Nominal framerate
                                  5.195e-5, // Active video time
                                  true) // Interlaced?
        { }

        public override ImageData Decode(double[] signal, int activeWidth, double bwMult = 1.0, double crosstalk = 0.0, double phError = 0.0, double phNoise = 0.0, double resonance = 1.0, double scanlineJitter = 0.0, int channelFlags = 0x7)
        {
            int[] activeSignalStarts = new int[videoScanlines]; // Start points of the active parts
            byte R = 0;
            byte G = 0;
            byte B = 0;
            double Y = 0.0;
            double U = 0.0;
            double V = 0.0;
            int polarity = 0;
            int pos = 0;
            int posdel = 0;
            double sigNum = 0.0;
            double sampleRate = activeWidth / realActiveTime; // Correction for the fact that the signal we've created only has active scanlines.
            double blendStr = 1.0 - crosstalk;
            bool inclY = ((channelFlags & 0x1) == 0) ? false : true;
            bool inclU = ((channelFlags & 0x2) == 0) ? false : true;
            bool inclV = ((channelFlags & 0x4) == 0) ? false : true;

            double sampleTime = realActiveTime / (double)activeWidth;
            FIRFilter mainfir = MathUtil.MakeFIRFilter(sampleRate, 256, ((mainBandwidth - sideBandwidth) / 2.0) * bwMult, (mainBandwidth + sideBandwidth) * bwMult, resonance);
            FIRFilter colfir = MathUtil.MakeFIRFilter(sampleRate, 256, ((chromaBandwidthUpper - chromaBandwidthLower) / 2.0) * bwMult, (chromaBandwidthLower + chromaBandwidthUpper) * bwMult, resonance);
            FIRFilter notchfir = new FIRFilter(colfir.forwardLen, colfir.backport);
            for (int i = -notchfir.backport; i < notchfir.forwardLen; i++)
            {
                notchfir[i] = -colfir[i];
            }
            notchfir[0] = 1.0 - colfir[0];
            double[] colsignal = MathUtil.FIRFilterCrosstalkShift(signal, colfir, crosstalk, sampleTime, carrierAngFreq);
            signal = MathUtil.FIRFilter(signal, mainfir);
            double[] USignal = new double[signal.Length];
            double[] VSignal = new double[signal.Length];
            double[] USignalPreAlt = new double[signal.Length];
            double[] VSignalPreAlt = new double[signal.Length];

            double time = 0.0;
            double phoffs = 0.0;
            Random rng = new Random();
            for (int i = 0; i < videoScanlines; i++)
            {
                phoffs = (2.0 * (rng.NextDouble() - 0.5) * phNoise + phError) * (Math.PI / 180.0);
                while (pos < boundPoints[i + 1])
                {
                    time = pos * sampleTime;
                    USignalPreAlt[pos] = colsignal[pos] * Math.Sin(carrierAngFreq * time + phoffs) * 2.0;
                    VSignalPreAlt[pos] = colsignal[pos] * Math.Cos(carrierAngFreq * time + phoffs) * 2.0;
                    pos++;
                }
            }

            signal = MathUtil.FIRFilterCrosstalkShift(signal, notchfir, crosstalk, sampleTime, carrierAngFreq);
            USignalPreAlt = MathUtil.FIRFilter(USignalPreAlt, colfir);
            VSignalPreAlt = MathUtil.FIRFilter(VSignalPreAlt, colfir);

            ImageData writeToSurface = new ImageData();
            writeToSurface.Width = activeWidth;
            writeToSurface.Height = videoScanlines;
            writeToSurface.Data = new byte[activeWidth * videoScanlines * 4];

            for (int i = 0; i < videoScanlines; i++) // Where the active signal starts
            {
                activeSignalStarts[i] = (int)((((double)i * (double)signal.Length) / (double)videoScanlines) + ((scanlineTime - realActiveTime) / (2 * realActiveTime)) * activeWidth);
            }

            pos = 0;

            if (isInterlaced) // Account for phase alternation
            {
                double alt = 0.0;
                pos = activeSignalStarts[0];
                for (int j = 0; j < writeToSurface.Width; j++) //We assume the chroma signal in all blanking periods is zero
                {
                    USignal[pos] = USignalPreAlt[pos] / 2.0;
                    VSignal[pos] = VSignalPreAlt[pos] / 2.0;
                    pos++;
                    posdel++;
                }
                for (int i = 1; i < videoScanlines / 2; i++) //Simulate a delay line
                {
                    pos = activeSignalStarts[i];
                    posdel = activeSignalStarts[i - 1];
                    alt = (i % 2) == 0 ? -1.0 : 1.0;
                    for (int j = 0; j < writeToSurface.Width; j++)
                    {
                        USignal[pos] = (USignalPreAlt[posdel] + USignalPreAlt[pos]) / 2.0;
                        VSignal[pos] = alt * (VSignalPreAlt[posdel] - VSignalPreAlt[pos]) / 2.0;
                        pos++;
                        posdel++;
                    }
                }
                pos = activeSignalStarts[videoScanlines / 2]; // If interlaced, there would be a large gap between one field and the next
                for (int j = 0; j < writeToSurface.Width; j++)
                {
                    USignal[pos] = USignalPreAlt[pos] / 2.0;
                    VSignal[pos] = VSignalPreAlt[pos] / 2.0;
                    pos++;
                    posdel++;
                }
                for (int i = (videoScanlines / 2) + 1; i < videoScanlines; i++) // Simulate a delay line
                {
                    pos = activeSignalStarts[i];
                    posdel = activeSignalStarts[i - 1];
                    alt = (i % 2) == 0 ? -1.0 : 1.0;
                    for (int j = 0; j < writeToSurface.Width; j++)
                    {
                        USignal[pos] = (USignalPreAlt[posdel] + USignalPreAlt[pos]) / 2.0;
                        VSignal[pos] = alt * (VSignalPreAlt[posdel] - VSignalPreAlt[pos]) / 2.0;
                        pos++;
                        posdel++;
                    }
                }
            }
            else
            {
                double alt = 0.0;
                pos = activeSignalStarts[0];
                for (int j = 0; j < writeToSurface.Width; j++) // We assume the chroma signal in all blanking periods is zero
                {
                    USignal[pos] = USignalPreAlt[pos] / 2.0;
                    VSignal[pos] = VSignalPreAlt[pos] / 2.0;
                    pos++;
                    posdel++;
                }
                for (int i = 1; i < videoScanlines; i++) // Simulate a delay line
                {
                    pos = activeSignalStarts[i];
                    posdel = activeSignalStarts[i - 1];
                    alt = (i % 2) == 0 ? -1.0 : 1.0;
                    for (int j = 0; j < writeToSurface.Width; j++)
                    {
                        USignal[pos] = (USignalPreAlt[posdel] + USignalPreAlt[pos]) / 2.0;
                        VSignal[pos] = alt * (VSignalPreAlt[posdel] - VSignalPreAlt[pos]) / 2.0;
                        pos++;
                        posdel++;
                    }
                }
            }

            byte[] surfaceColors = writeToSurface.Data;
            int currentScanline;
            int curjit = 0;
            double dR = 0.0;
            double dG = 0.0;
            double dB = 0.0;
            for (int i = 0; i < videoScanlines; i++)
            {
                if (i * 2 >= videoScanlines) // Simulate interlacing
                {
                    polarity = 1;
                }
                currentScanline = isInterlaced ? (i * 2 + polarity) % videoScanlines : i;
                curjit = (int)(scanlineJitter * 2.0 * (rng.NextDouble() - 0.5) * activeWidth);
                pos = activeSignalStarts[i] + curjit;

                for (int j = 0; j < writeToSurface.Width; j++) // Decode active signal region only
                {
                    Y = inclY ? signal[pos] : 0.5;
                    U = inclU ? USignal[pos] : 0.0;
                    V = inclV ? VSignal[pos] : 0.0;
                    dR = Math.Pow(YUVtoRGBConversionMatrix[0] * Y + YUVtoRGBConversionMatrix[2] * V, PALGamma);
                    dG = Math.Pow(YUVtoRGBConversionMatrix[3] * Y + YUVtoRGBConversionMatrix[4] * U + YUVtoRGBConversionMatrix[5] * V, PALGamma);
                    dB = Math.Pow(YUVtoRGBConversionMatrix[6] * Y + YUVtoRGBConversionMatrix[7] * U, PALGamma);
                    R = (byte)(MathUtil.Clamp(MathUtil.SRGBInverseGammaTransform(dR), 0.0, 1.0) * 255.0);
                    G = (byte)(MathUtil.Clamp(MathUtil.SRGBInverseGammaTransform(dG), 0.0, 1.0) * 255.0);
                    B = (byte)(MathUtil.Clamp(MathUtil.SRGBInverseGammaTransform(dB), 0.0, 1.0) * 255.0);
                    surfaceColors[(currentScanline * writeToSurface.Width + j) * 4 + 3] = 255;
                    surfaceColors[(currentScanline * writeToSurface.Width + j) * 4 + 2] = R;
                    surfaceColors[(currentScanline * writeToSurface.Width + j) * 4 + 1] = G;
                    surfaceColors[(currentScanline * writeToSurface.Width + j) * 4] = B;
                    pos++;
                }
            }
            return writeToSurface;
        }

        public override double[] Encode(ImageData surface)
        {
            // To get a good analog feel, we must limit the vertical resolution; the horizontal
            // resolution will be limited as we decode the distorted signal.
            int signalLen = (int)(surface.Width * videoScanlines * (scanlineTime / realActiveTime));
            int[] boundaryPoints = new int[videoScanlines + 1]; // Boundaries of the scanline signals
            int[] activeSignalStarts = new int[videoScanlines]; // Start points of the active parts
            double[] signalOut = new double[signalLen];
            double R = 0.0;
            double G = 0.0;
            double B = 0.0;
            double U = 0.0;
            double V = 0.0;
            double time = 0;
            int pos = 0;
            int polarity = 0;
            double phaseAlternate = 1.0; // Why this is called PAL in the first place
            int remainingSync = 0;
            double sampleTime = realActiveTime / (double)surface.Width;

            boundaryPoints[0] = 0; // Beginning of the signal
            boundaryPoints[videoScanlines] = signalLen; // End of the signal
            for (int i = 1; i < videoScanlines; i++) // Rest of the signal
            {
                boundaryPoints[i] = (i * signalLen) / videoScanlines;
            }

            boundPoints = boundaryPoints;

            for (int i = 0; i < videoScanlines; i++) // Where the active signal starts
            {
                activeSignalStarts[i] = (int)((((double)i * (double)signalLen) / (double)videoScanlines) + ((scanlineTime - realActiveTime) / (2 * realActiveTime)) * surface.Width) - boundaryPoints[i];
            }

            byte[] surfaceColors = surface.Data;
            int currentScanline;
            for (int i = 0; i < videoScanlines; i++) // Only generate active scanlines
            {
                if (i * 2 >= videoScanlines) // Simulate interlacing
                {
                    polarity = 1;
                }
                currentScanline = isInterlaced ? (i * 2 + polarity) % videoScanlines : i;
                if ((i % 2) == 1) // Do phase alternation
                {
                    phaseAlternate = -1.0;
                }
                else phaseAlternate = 1.0;
                for (int j = 0; j < activeSignalStarts[i]; j++) // Front porch, ignore sync signal because we don't see its results
                {
                    signalOut[pos] = 0.0;
                    pos++;
                    time = pos * sampleTime;
                }
                for (int j = 0; j < surface.Width; j++) // Active signal
                {
                    signalOut[pos] = 0f;
                    R = surfaceColors[(currentScanline * surface.Width + j) * 4 + 2] / 255.0;
                    G = surfaceColors[(currentScanline * surface.Width + j) * 4 + 1] / 255.0;
                    B = surfaceColors[(currentScanline * surface.Width + j) * 4] / 255.0;
                    R = MathUtil.SRGBGammaTransform(R);
                    G = MathUtil.SRGBGammaTransform(G);
                    B = MathUtil.SRGBGammaTransform(B);
                    R = Math.Pow(R, 1.0 / PALGamma); // Gamma correction
                    G = Math.Pow(G, 1.0 / PALGamma);
                    B = Math.Pow(B, 1.0 / PALGamma);
                    U = RGBtoYUVConversionMatrix[3] * R + RGBtoYUVConversionMatrix[4] * G + RGBtoYUVConversionMatrix[5] * B; // Encode U and V
                    V = RGBtoYUVConversionMatrix[6] * R + RGBtoYUVConversionMatrix[7] * G + RGBtoYUVConversionMatrix[8] * B;
                    signalOut[pos] += RGBtoYUVConversionMatrix[0] * R + RGBtoYUVConversionMatrix[1] * G + RGBtoYUVConversionMatrix[2] * B; //Add luma straightforwardly
                    signalOut[pos] += U * Math.Sin(carrierAngFreq * time) + phaseAlternate * V * Math.Cos(carrierAngFreq * time); // Add chroma via QAM
                    pos++;
                    time = pos * sampleTime;
                }
                while (pos < boundaryPoints[i + 1]) // Back porch, ignore sync signal because we don't see its results
                {
                    signalOut[pos] = 0.0;
                    pos++;
                    time = pos * sampleTime;
                }
            }
            return signalOut;
        }
    }
}
