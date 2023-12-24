using PaintDotNet;
using System.Numerics;

namespace AnalogTVFilter
{
    public class NTSCFormat : AnalogFormat
    {
        // The NTSC format, used in America, etc.
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

        public override Surface Decode(double[] signal, int activeWidth, double crosstalk = 0.0, double resonance = 1.0, double scanlineJitter = 0.0, int channelFlags = 0x7)
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
            double sampleRate = signal.Length / frameTime;
            double blendStr = 1.0 - crosstalk;
            double c = Math.Cos(chromaPhase);
            double s = Math.Sin(chromaPhase);
            bool inclY = ((channelFlags & 0x1) == 0) ? false : true;
            bool inclQ = ((channelFlags & 0x2) == 0) ? false : true;
            bool inclI = ((channelFlags & 0x4) == 0) ? false : true;

            Complex[] signalFT = MathUtil.FourierTransform(signal, 1);
            signalFT = MathUtil.BandPassFilter(signalFT, sampleRate, (mainBandwidth - sideBandwidth) / 2.0, mainBandwidth + sideBandwidth, resonance); // Restrict bandwidth to the actual broadcast bandwidth
            Complex[] QcolorSignalFT = MathUtil.BandPassFilter(signalFT, sampleRate, chromaCarrierFrequency, 2 * chromaBandwidthUpper, resonance, blendStr); // Extract color information
            Complex[] IcolorSignalFT = MathUtil.BandPassFilter(signalFT, sampleRate, ((chromaBandwidthUpper - chromaBandwidthLower) / 2.0) + chromaCarrierFrequency, chromaBandwidthLower + chromaBandwidthUpper, resonance, blendStr); //Q has less resolution than I
            QcolorSignalFT = MathUtil.ShiftArrayInterp(QcolorSignalFT, ((chromaCarrierFrequency - 306820.0) / sampleRate) * QcolorSignalFT.Length); //apologies for the fudge factor
            IcolorSignalFT = MathUtil.ShiftArrayInterp(IcolorSignalFT, ((((chromaBandwidthUpper - chromaBandwidthLower) / 2.0) + chromaCarrierFrequency + 33180.0) / sampleRate) * IcolorSignalFT.Length); //apologies for the fudge factor
            Complex[] QSignalIFT = MathUtil.InverseFourierTransform(QcolorSignalFT);
            Complex[] ISignalIFT = MathUtil.InverseFourierTransform(IcolorSignalFT);
            double[] QSignal = new double[signal.Length];
            double[] ISignal = new double[signal.Length];
            signalFT = MathUtil.NotchFilter(signalFT, sampleRate, ((chromaBandwidthUpper - chromaBandwidthLower) / 2.0) + chromaCarrierFrequency, chromaBandwidthLower + chromaBandwidthUpper, resonance, blendStr);
            Complex[] finalSignal = MathUtil.InverseFourierTransform(signalFT);

            for (int i = 0; i < signal.Length; i++)
            {
                signal[i] = 1.0 * finalSignal[finalSignal.Length - 1 - i].Real;
                QSignal[i] = 2.0 * (-c * (QSignalIFT[finalSignal.Length - 1 - i].Imaginary) + s * (QSignalIFT[finalSignal.Length - 1 - i].Real));
                ISignal[i] = 2.0 * (c * (ISignalIFT[finalSignal.Length - 1 - i].Real) + s * (ISignalIFT[finalSignal.Length - 1 - i].Imaginary));
            }

            Surface writeToSurface = new Surface(activeWidth, videoScanlines);

            for (int i = 0; i < videoScanlines; i++) // Where the active signal starts
            {
                activeSignalStarts[i] = (int)((((double)i * (double)signal.Length) / (double)videoScanlines) + ((scanlineTime - realActiveTime) / (2 * realActiveTime)) * activeWidth);
            }

            MemoryBlock surfaceColors = writeToSurface.Scan0;
            int currentScanline;
            Random rng = new Random();
            int curjit = 0;
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
                    R = (byte)(MathUtil.Clamp(Math.Pow(YUVtoRGBConversionMatrix[0] * Y + YUVtoRGBConversionMatrix[1] * Q + YUVtoRGBConversionMatrix[2] * I, 0.4545), 0.0, 1.0) * 255.0);
                    G = (byte)(MathUtil.Clamp(Math.Pow(YUVtoRGBConversionMatrix[3] * Y + YUVtoRGBConversionMatrix[4] * Q + YUVtoRGBConversionMatrix[5] * I, 0.4545), 0.0, 1.0) * 255.0);
                    B = (byte)(MathUtil.Clamp(Math.Pow(YUVtoRGBConversionMatrix[6] * Y + YUVtoRGBConversionMatrix[7] * Q + YUVtoRGBConversionMatrix[8] * I, 0.4545), 0.0, 1.0) * 255.0);
                    surfaceColors[(currentScanline * writeToSurface.Width + j) * 4 + 3] = 255;
                    surfaceColors[(currentScanline * writeToSurface.Width + j) * 4 + 2] = R;
                    surfaceColors[(currentScanline * writeToSurface.Width + j) * 4 + 1] = G;
                    surfaceColors[(currentScanline * writeToSurface.Width + j) * 4] = B;
                    pos++;
                }
            }

            return writeToSurface;
        }

        public override double[] Encode(Surface surface)
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

            Surface wrkSrf = new Surface(surface.Width, videoScanlines);
            wrkSrf.FitSurface(ResamplingAlgorithm.SuperSampling, surface);

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

            MemoryBlock surfaceColors = wrkSrf.Scan0;
            int currentScanline;
            for (int i = 0; i < videoScanlines; i++)
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
                    R = Math.Pow(R, 2.2); // Gamma correction
                    G = Math.Pow(G, 2.2);
                    B = Math.Pow(B, 2.2);
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
