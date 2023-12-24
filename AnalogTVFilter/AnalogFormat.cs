using PaintDotNet;

namespace AnalogTVFilter
{
    // Base class for all analog formats
    public abstract class AnalogFormat
    {
        // Basicc parameters
        protected readonly double RtoYFactor;
        protected readonly double GtoYFactor;
        protected readonly double BtoYFactor;
        protected readonly double UMax;
        protected readonly double VMax;
        protected readonly double chromaPhase;
        protected readonly double mainBandwidth;
        protected readonly double sideBandwidth;
        protected readonly double chromaBandwidthLower;
        protected readonly double chromaBandwidthUpper;
        protected readonly double chromaCarrierFrequency;
        protected readonly int scanlines;
        protected readonly int videoScanlines;
        protected readonly double framerate;
        protected bool isInterlaced;
        protected readonly double activeTime;
        // Derived parameters
        protected readonly double carrierAngFreq;
        protected readonly double[] RGBtoYUVConversionMatrix;
        protected readonly double[] YUVtoRGBConversionMatrix;
        protected double frameTime;
        protected double scanlineTime;
        protected double realActiveTime;
        protected int[] boundPoints;

        public int Scanlines { get { return scanlines; } }
        public double Framerate { get { return isInterlaced ? framerate / 2f : framerate; } }
        public double SubcarrierFrequency { get { return chromaCarrierFrequency; } }
        public int[] BoundaryPoints { get { return boundPoints; } }

        public AnalogFormat(double RtoY, double GtoY, double BtoY, double U, double V, double phase, double bWidth, double sWidth, double cWidthL, double cWidthU, double cFreq, int lines, int vlines, double fRate, double aTime, bool interlace)
        {
            RtoYFactor = RtoY;
            GtoYFactor = GtoY;
            BtoYFactor = BtoY;
            UMax = U;
            VMax = V;
            chromaPhase = phase;
            mainBandwidth = bWidth;
            sideBandwidth = sWidth;
            chromaBandwidthLower = cWidthL;
            chromaBandwidthUpper = cWidthU;
            chromaCarrierFrequency = cFreq;
            scanlines = lines;
            videoScanlines = vlines;
            framerate = fRate;
            isInterlaced = interlace;
            activeTime = aTime;
            double c = Math.Cos(phase);
            double s = Math.Sin(phase);

            /* Layout:
             * | 0 1 2 |
             * | 3 4 5 |
             * | 6 7 8 |
             */
            RGBtoYUVConversionMatrix = new double[9];
            RGBtoYUVConversionMatrix[0] = RtoYFactor;
            RGBtoYUVConversionMatrix[1] = GtoYFactor;
            RGBtoYUVConversionMatrix[2] = BtoYFactor;
            RGBtoYUVConversionMatrix[3] = -(UMax * c * RtoYFactor / (1.0 - BtoYFactor)) + s * VMax;
            RGBtoYUVConversionMatrix[4] = -(UMax * c * GtoYFactor / (1.0 - BtoYFactor)) - (VMax * s * GtoYFactor / (1.0 - RtoYFactor));
            RGBtoYUVConversionMatrix[5] = UMax * c - (VMax * s * BtoYFactor / (1.0 - RtoYFactor));
            RGBtoYUVConversionMatrix[6] = VMax * c + (UMax * s * RtoYFactor / (1.0 - BtoYFactor));
            RGBtoYUVConversionMatrix[7] = -(VMax * c * GtoYFactor / (1.0 - RtoYFactor)) + (UMax * s * GtoYFactor / (1.0 - BtoYFactor));
            RGBtoYUVConversionMatrix[8] = -(VMax * c * BtoYFactor / (1.0 - RtoYFactor)) - UMax * s;

            YUVtoRGBConversionMatrix = new double[9]; // Specialized inverse, not a general matrix inversion
            YUVtoRGBConversionMatrix[0] = 1.0;
            YUVtoRGBConversionMatrix[1] = s * (1.0 - RtoYFactor) / VMax;
            YUVtoRGBConversionMatrix[2] = c * (1.0 - RtoYFactor) / VMax;
            YUVtoRGBConversionMatrix[3] = 1.0;
            YUVtoRGBConversionMatrix[4] = -(BtoYFactor * c * (1.0 - BtoYFactor) / (UMax * GtoYFactor)) - (RtoYFactor * s * (1.0 - RtoYFactor) / (VMax * GtoYFactor));
            YUVtoRGBConversionMatrix[5] = -RtoYFactor * c * (1.0 - RtoYFactor) / (VMax * GtoYFactor) + (BtoYFactor * s * (1.0 - BtoYFactor) / (UMax * GtoYFactor));
            YUVtoRGBConversionMatrix[6] = 1.0;
            YUVtoRGBConversionMatrix[7] = c * (1.0 - BtoYFactor) / UMax;
            YUVtoRGBConversionMatrix[8] = -s * (1.0 - BtoYFactor) / UMax;

            frameTime = (isInterlaced ? 2.0 : 1.0) / framerate;
            scanlineTime = (isInterlaced ? 2.0 : 1.0) / (double)(scanlines * framerate);
            realActiveTime = activeTime / (isInterlaced ? 1.0 : 2.0);
            carrierAngFreq = 2 * Math.PI * chromaCarrierFrequency;
        }

        public void SetInterlace(bool interlace)
        {
            isInterlaced = interlace;
            frameTime = (isInterlaced ? 2.0 : 1.0) / framerate;
            scanlineTime = (isInterlaced ? 2.0 : 1.0) / (double)(scanlines * framerate);
            realActiveTime = activeTime / (isInterlaced ? 1.0 : 2.0);
        }

        public abstract double[] Encode(Surface surface);
        public abstract Surface Decode(double[] signal, int activeWidth, double crosstalk, double resonance, double scanlineJitter, int channelFlags); //Decode must respect the original bandwidths, otherwise we don't get that analog feeling
    }
}
