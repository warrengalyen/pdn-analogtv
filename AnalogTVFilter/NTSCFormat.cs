﻿/*
 * Implementation of the NTSC format
 * 
 * Dependency: MathUtil.cs
 * 
 * 2023 Warren Galyen
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
    // The NTSC format, used in America, etc.
    public class NTSCFormat : AnalogFormat
    {
        const double NTSCGamma = 2.2;

        public NTSCFormat() : base(0.299, // R to Y
                                   0.587, // G to Y
                                   0.114, // B to Y
                                   0.436, // Q maximum
                                   0.615, // I maximum
                                   33.0 * (Math.PI / 180.0), // Chroma conversion phase relative to YUV
                                   4.2e+6, // Main bandwidth
                                   1e+6, // Side bandwidth
                                   1.3e+6, // Color bandwidth lower part
                                   0.62e+6, // Color bandwidth upper part
                                   3579545.0, // Color subcarrier frequency
                                   525, // Total scanlines
                                   480, // Visible scanlines
                                   59.94005994, // Nominal framerate
                                   5.26555e-5, // Active video time
                                   true) // Interlaced?
        { }

        public override ImageData Decode(double[] signal, int activeWidth, double crosstalk = 0.0, double resonance = 1.0, double scanlineJitter = 0.0, double monitorGamma = 2.5, int channelFlags = 0x7)
        {
            int[] activeSignalStarts = new int[videoScanlines]; // Start points of the active parts
            byte R = 0;
            byte G = 0;
            byte B = 0;
            double Y = 0.0;
            double Q = 0.0;
            double I = 0.0;
            int polarity = 0;
            int pos = 0;
            int posdel = 0;
            double sigNum = 0.0;
            double sampleRate = ((double)signal.Length * (((double)scanlines) / ((double)videoScanlines))) / frameTime; // Correction for the fact that the signal we've created only has active scanlines.
            double blendStr = 1.0 - crosstalk;
            double c = Math.Cos(chromaPhase);
            double s = Math.Sin(chromaPhase);
            bool inclY = ((channelFlags & 0x1) == 0) ? false : true;
            bool inclQ = ((channelFlags & 0x2) == 0) ? false : true;
            bool inclI = ((channelFlags & 0x4) == 0) ? false : true;

            double sampleTime = realActiveTime / (double)activeWidth;
            double[] mainfir = MathUtil.MakeFIRFilter(sampleRate, 80, (mainBandwidth - sideBandwidth) / 2.0, mainBandwidth + sideBandwidth, resonance);
            double[] qfir = MathUtil.MakeFIRFilter(sampleRate, 80, 0.0, 2.0 * chromaBandwidthUpper, resonance); //Q has less resolution than I
            double[] ifir = MathUtil.MakeFIRFilter(sampleRate, 80, (chromaBandwidthUpper - chromaBandwidthLower) / 2.0, chromaBandwidthLower + chromaBandwidthUpper, resonance);
            for (int i = 1; i < qfir.Length; i++)
            {
                qfir[i] *= 2.0;
            }
            for (int i = 1; i < ifir.Length; i++)
            {
                ifir[i] *= 2.0;
            }
            double[] notchfir = new double[qfir.Length];
            notchfir[0] = 1.0 - qfir[0];
            for (int i = 1; i < notchfir.Length; i++)
            {
                notchfir[i] = -qfir[i];
            }
            signal = MathUtil.FIRFilter(signal, mainfir);
            double[] QSignal = MathUtil.FIRFilterCrosstalkShift(signal, qfir, crosstalk, sampleTime, carrierAngFreq);
            double[] ISignal = MathUtil.FIRFilterCrosstalkShift(signal, ifir, crosstalk, sampleTime, carrierAngFreq);
            double time = 0.0;
            for (int i = 0; i < signal.Length; i++)
            {
                time = i * sampleTime;
                QSignal[i] = QSignal[i] * Math.Sin(carrierAngFreq * time - 0.25 * Math.PI + chromaPhase) * MathUtil.sqrt2;
                ISignal[i] = ISignal[i] * Math.Cos(carrierAngFreq * time - 0.25 * Math.PI + chromaPhase) * MathUtil.sqrt2;
            }
            signal = MathUtil.FIRFilterCrosstalkShift(signal, notchfir, crosstalk, sampleTime, carrierAngFreq);
            QSignal = MathUtil.FIRFilter(QSignal, qfir);
            ISignal = MathUtil.FIRFilter(ISignal, ifir);

            ImageData writeToSurface = new ImageData();
            writeToSurface.Width = activeWidth;
            writeToSurface.Height = videoScanlines;
            writeToSurface.Data = new byte[activeWidth * videoScanlines * 4];

            for (int i = 0; i < videoScanlines; i++) // Where the active signal starts
            {
                activeSignalStarts[i] = (int)((((double)i * (double)signal.Length) / (double)videoScanlines) + ((scanlineTime - realActiveTime) / (2 * realActiveTime)) * activeWidth);
            }

            byte[] surfaceColors = writeToSurface.Data;
            int currentScanline;
            Random rng = new Random();
            int curjit = 0;
            double gammaFactor = monitorGamma / NTSCGamma;
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
                    Q = inclQ ? QSignal[pos] : 0.0;
                    I = inclI ? ISignal[pos] : 0.0;
                    R = (byte)(MathUtil.Clamp(Math.Pow(YUVtoRGBConversionMatrix[0] * Y + YUVtoRGBConversionMatrix[1] * Q + YUVtoRGBConversionMatrix[2] * I, gammaFactor), 0.0, 1.0) * 255.0);
                    G = (byte)(MathUtil.Clamp(Math.Pow(YUVtoRGBConversionMatrix[3] * Y + YUVtoRGBConversionMatrix[4] * Q + YUVtoRGBConversionMatrix[5] * I, gammaFactor), 0.0, 1.0) * 255.0);
                    B = (byte)(MathUtil.Clamp(Math.Pow(YUVtoRGBConversionMatrix[6] * Y + YUVtoRGBConversionMatrix[7] * Q + YUVtoRGBConversionMatrix[8] * I, gammaFactor), 0.0, 1.0) * 255.0);
                    surfaceColors[(currentScanline * writeToSurface.Width + j) * 4 + 3] = 255;
                    surfaceColors[(currentScanline * writeToSurface.Width + j) * 4 + 2] = R;
                    surfaceColors[(currentScanline * writeToSurface.Width + j) * 4 + 1] = G;
                    surfaceColors[(currentScanline * writeToSurface.Width + j) * 4] = B;
                    pos++;
                }
            }

            return writeToSurface;
        }

        public override double[] Encode(ImageData surface, double monitorGamma = 2.5)
        {
            int signalLen = (int)(surface.Width * videoScanlines * (scanlineTime / realActiveTime)); // To get a good analog feel, we must limit the vertical resolution; the horizontal resolution will be limited as we decode the distorted signal.
            int[] boundaryPoints = new int[videoScanlines + 1]; // Boundaries of the scanline signals
            int[] activeSignalStarts = new int[videoScanlines]; // Start points of the active parts
            double[] signalOut = new double[signalLen];
            double R = 0.0;
            double G = 0.0;
            double B = 0.0;
            double Q = 0.0;
            double I = 0.0;
            double time = 0.0;
            int pos = 0;
            int polarity = 0;
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
            double gammaFactor = NTSCGamma / monitorGamma;
            for (int i = 0; i < videoScanlines; i++)  // Only generate active scanlines
            {
                if (i * 2 >= videoScanlines) // Simulate interlacing
                {
                    polarity = 1;
                }
                currentScanline = isInterlaced ? (i * 2 + polarity) % videoScanlines : i;
                for (int j = 0; j < activeSignalStarts[i]; j++) // Front porch, ignore sync signal because we don't see its results
                {
                    signalOut[pos] = 0.0;
                    pos++;
                    time = pos * sampleTime;
                }
                for (int j = 0; j < surface.Width; j++) // Active signal
                {
                    signalOut[pos] = 0.0;
                    R = surfaceColors[(currentScanline * surface.Width + j) * 4 + 2] / 255.0;
                    G = surfaceColors[(currentScanline * surface.Width + j) * 4 + 1] / 255.0;
                    B = surfaceColors[(currentScanline * surface.Width + j) * 4] / 255.0;
                    R = Math.Pow(R, gammaFactor); // Gamma correction
                    G = Math.Pow(G, gammaFactor);
                    B = Math.Pow(B, gammaFactor);
                    Q = RGBtoYUVConversionMatrix[3] * R + RGBtoYUVConversionMatrix[4] * G + RGBtoYUVConversionMatrix[5] * B; // Encode Q and I
                    I = RGBtoYUVConversionMatrix[6] * R + RGBtoYUVConversionMatrix[7] * G + RGBtoYUVConversionMatrix[8] * B;
                    signalOut[pos] += RGBtoYUVConversionMatrix[0] * R + RGBtoYUVConversionMatrix[1] * G + RGBtoYUVConversionMatrix[2] * B; //Add luma straightforwardly
                    signalOut[pos] += Q * Math.Sin(carrierAngFreq * time + chromaPhase) + I * Math.Cos(carrierAngFreq * time + chromaPhase); //Add chroma via QAM
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
