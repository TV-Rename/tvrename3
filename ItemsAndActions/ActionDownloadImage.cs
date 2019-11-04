// 
// Main website for TVRename is http://tvrename.com
// 
// Source code available at https://github.com/TV-Rename/tvrename
// 
// Copyright (c) TV Rename. This code is released under GPLv3 https://github.com/TV-Rename/tvrename/blob/master/LICENSE.md
// 


using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using JetBrains.Annotations;

namespace TVRename
{
    using System;
    using Alphaleonis.Win32.Filesystem;

    public class ActionDownloadImage : ActionDownload
    {
        private readonly string path;
        private readonly FileInfo destination;
        private readonly ShowItem si;
        private readonly bool shrinkLargeMede8ErImage;

        public ActionDownloadImage(ShowItem si, ProcessedEpisode pe, FileInfo dest, string path) : this(si, pe, dest, path, false) { }

        public ActionDownloadImage(ShowItem si, ProcessedEpisode pe, FileInfo dest, string path, bool shrink)
        {
            Episode = pe;
            this.si = si;
            destination = dest;
            this.path = path;
            shrinkLargeMede8ErImage = shrink;
        }

        #region Action Members

        [NotNull]
        public override string Name => "Download";

        public override string ProgressText => destination.Name;

        public override string Produces => destination.FullName;

        // 0 to 100
        public override long SizeOfWork => 1000000;

        public override bool Go(ref bool pause, TVRenameStats stats)
        {
            byte[] theData = TheTVDB.Instance.GetTvdbDownload(path);
            if ((theData is null) || (theData.Length == 0))
            {
                ErrorText = "Unable to download " + path;
                Error = true;
                Done = true;
                return false;
            }

            if (shrinkLargeMede8ErImage)
            {
                using (Stream streamPhoto = new MemoryStream(theData))
                {
                    BitmapFrame bfPhoto = ReadBitmapFrame(streamPhoto);

                    if (Episode is null)
                    {
                        if ((bfPhoto.Width > 156) || (bfPhoto.Height > 232))
                        {
                            theData = Resize(bfPhoto, 156, 232);
                        }
                    }
                    else
                    {
                        if ((bfPhoto.Width > 232) || (bfPhoto.Height > 156))
                        {
                            theData = Resize(bfPhoto, 232,156);
                        }
                    }
                }
            }

            try
            {
                FileStream fs = new FileStream(destination.FullName, FileMode.Create);
                fs.Write(theData, 0, theData.Length);
                fs.Close();
            }
            catch (Exception e)
            {
                ErrorText = e.Message;
                Error = true;
                Done = true;
                return false;
            }

            Done = true;
            return true;
        }

        private byte[] Resize(BitmapFrame bfPhoto, int width, int height)
        {

            double sourceWidth = bfPhoto.Width;
            double sourceHeight = bfPhoto.Height;

            float nPercentW = (width / (float)sourceWidth);
            float nPercentH = (height / (float)sourceHeight);

            int destWidth, destHeight;

            if (nPercentH < nPercentW)
            {
                destHeight = height;
                destWidth = (int)(sourceWidth * nPercentH);
            }
            else
            {
                destHeight = (int)(sourceHeight * nPercentW);
                destWidth = width;
            }

            BitmapFrame bfResize = FastResize(bfPhoto, destWidth, destHeight);

            return ToByteArray(bfResize);
        }

        private static BitmapFrame FastResize(BitmapFrame bfPhoto, int nWidth, int nHeight)
            {
                TransformedBitmap tbBitmap = new TransformedBitmap(bfPhoto, new ScaleTransform(nWidth / bfPhoto.Width, nHeight / bfPhoto.Height, 0, 0));
                return BitmapFrame.Create(tbBitmap);
            }

            private static byte[] ToByteArray(BitmapFrame bfResize)
            {
                using (MemoryStream msStream = new MemoryStream())
                {
                    PngBitmapEncoder pbdDecoder = new PngBitmapEncoder();
                    pbdDecoder.Frames.Add(bfResize);
                    pbdDecoder.Save(msStream);
                    return msStream.ToArray();
                }
            }

            private static BitmapFrame ReadBitmapFrame(Stream streamPhoto)
            {
                BitmapDecoder bdDecoder = BitmapDecoder.Create(streamPhoto, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.None);
                return bdDecoder.Frames[0];
            }

        #endregion

            #region Item Members

        public override bool SameAs(Item o)
        {
            return (o is ActionDownloadImage image) && (image.destination == destination);
        }

        public override int Compare(Item o)
        {
            return !(o is ActionDownloadImage dl) ? 0 : string.Compare(destination.FullName, dl.destination.FullName, StringComparison.Ordinal);
        }

        #endregion

        #region Item Members

        public override int IconNumber => 5;

        [CanBeNull]
        public override IgnoreItem Ignore => GenerateIgnore(destination?.FullName);

        protected override string SeriesName =>
            (Episode != null) ? Episode.Show.ShowName : ((si != null) ? si.ShowName : "");

        [CanBeNull]
        protected override string DestinationFolder => TargetFolder;
        protected override string DestinationFile => destination.Name;
        protected override string SourceDetails => path;
        protected override bool InError => string.IsNullOrEmpty(path);
        [NotNull]
        public override string ScanListViewGroup => "lvgActionDownload";
        [CanBeNull]
        public override string TargetFolder => destination?.DirectoryName;
        #endregion
    }
}
