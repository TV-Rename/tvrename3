// 
// Main website for TVRename is http://tvrename.com
// 
// Source code available at https://github.com/TV-Rename/tvrename
// 
// Copyright (c) TV Rename. This code is released under GPLv3 https://github.com/TV-Rename/tvrename/blob/master/LICENSE.md
// 

using System;
using Alphaleonis.Win32.Filesystem;

namespace TVRename
{
    internal abstract class Exporter
    {
        protected static readonly NLog.Logger LOGGER = NLog.LogManager.GetCurrentClassLogger();

        public void Run()
        {
            if (!Active())
            {
                LOGGER.Trace("Skipped (Disabled) Output File to: {0}", Location());
                return;
            }
            if (string.IsNullOrWhiteSpace(Location()))
            {
                LOGGER.Warn("Please open settings and ensure filenames are provided for each exporter you have enabled");
                return;
            }
            if (Location().IsWebLink())
            {
                LOGGER.Warn($"TV Rename cannot export file to a web location: {Location()}, please update in the setttings");
                return;
            }

            try
            {
                //Create the directory if needed
                Directory.CreateDirectory(Path.GetDirectoryName(Location()) ?? string.Empty);
                Do();
                LOGGER.Info("Output File to: {0}", Location());
            }
            catch (NotSupportedException e)
            {
                LOGGER.Warn($"Output File must be a local file: {Location()} {e.Message}");
            }
            catch (System.IO.DirectoryNotFoundException e)
            {
                LOGGER.Warn($"Could not find File/Directory at: {Location()} {e.Message}");
            }
            catch (UnauthorizedAccessException e)
            {
                LOGGER.Warn($"Could not access File/Directory at: {Location()} {e.Message}");
            }
            catch (System.IO.IOException e)
            {
                LOGGER.Warn($"Could not access File/Directory at: {Location()} {e.Message}");
            }
            catch (Exception e)
            {
                LOGGER.Error(e, "Failed to Output File to: {0}", Location());
            }
        }

        public abstract bool Active();
        protected abstract string Location();
        protected abstract void Do();
    }
}
