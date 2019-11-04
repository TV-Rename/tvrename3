// 
// Main website for TVRename is http://tvrename.com
// 
// Source code available at https://github.com/TV-Rename/tvrename
// 
// Copyright (c) TV Rename. This code is released under GPLv3 https://github.com/TV-Rename/tvrename/blob/master/LICENSE.md
// 

using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Linq;
using JetBrains.Annotations;
using DirectoryInfo = Alphaleonis.Win32.Filesystem.DirectoryInfo;
using FileInfo = Alphaleonis.Win32.Filesystem.FileInfo;
using FileSystemInfo = Alphaleonis.Win32.Filesystem.FileSystemInfo;
using Path = Alphaleonis.Win32.Filesystem.Path;

namespace TVRename
{
    abstract class FileFinder : Finder
    {
        protected FileFinder(TVDoc i) : base(i) { }

        public override FinderDisplayType DisplayType() => FinderDisplayType.local;

        // ReSharper disable once FunctionComplexityOverflow
        protected bool ReviewFile(ItemMissing me, ItemList addTo, FileInfo dce, TVDoc.ScanSettings settings, bool addMergeRules,bool preventMove,bool doExtraFiles,bool useFullPath)
        {
            if (settings.Token.IsCancellationRequested)
            {
                return false;
            }

            int season = me.Episode.AppropriateSeasonNumber;
            int epnum = me.Episode.AppropriateEpNum;
            bool matched = false;

            try
            {
                if (dce.IgnoreFile())
                {
                    return false;
                }

                //do any of the possible names for the series match the filename?
                matched = me.Episode.Show.NameMatch(dce,useFullPath);

                if (!matched)
                {
                    return false;
                }

                bool regularMatch =
                    FinderHelper.FindSeasEp(dce, out int seasF, out int epF, out int maxEp, me.Episode.Show) &&
                    seasF == season &&
                    epF == epnum;

                bool sequentialMatch =
                    me.Episode.Show.UseSequentialMatch &&
                    TVDoc.MatchesSequentialNumber(dce.Name, ref seasF, ref epF, me.Episode) &&
                    seasF == season &&
                    epF == epnum;

                if (!regularMatch && !sequentialMatch)
                {
                    return false;
                }

                if (maxEp != -1 && addMergeRules)
                {
                    me = UpdateMissingItem(me, dce, epF, maxEp, seasF);
                }

                FileInfo fi = new FileInfo(me.TheFileNoExt + dce.Extension);

                if (preventMove)
                {
                    //We do not want to move the file, just rename it
                    fi = new FileInfo(dce.DirectoryName + Path.DirectorySeparatorChar + me.Filename +
                                      dce.Extension);
                }

                if ((dce.FullName != fi.FullName) && (!FindExistingActionFor(addTo,dce))){
                    // don't remove the base search folders
                    bool doTidyup =
                        !TVSettings.Instance.DownloadFolders.Any(folder =>
                            folder.SameDirectoryLocation(fi.Directory.FullName));

                    addTo.Add(new ActionCopyMoveRename(ActionCopyMoveRename.Op.copy, dce, fi, me.Episode, doTidyup,
                        me));
                }

                if (doExtraFiles)
                {
                    DownloadIdentifiersController di = new DownloadIdentifiersController();

                    // if we're copying/moving a file across, we might also want to make a thumbnail or NFO for it
                    addTo.Add(di.ProcessEpisode(me.Episode, fi));
                }

                return true;
            }
            catch (PathTooLongException e)
            {
                WarnPathTooLong(me, dce, e, matched, season, epnum);
            }
            return false;
        }

        private void WarnPathTooLong([NotNull] ItemMissing me, [NotNull] FileInfo dce, [NotNull] Exception e, bool matched, int season, int epnum)
        {
            string t = "Path too long. " + dce.FullName + ", " + e.Message;
            LOGGER.Error(e, "Path too long. " + dce.FullName);

            t += ".  More information is available in the log file";
            if ((!MDoc.Args.Unattended) && (!MDoc.Args.Hide) && Environment.UserInteractive)
            {
                MessageBox.Show(t, "Path too long", MessageBoxButton.OK, MessageBoxImage.Exclamation);
            }

            t = "DirectoryName " + dce.DirectoryName + ", File name: " + dce.Name;
            t += matched ? ", matched.  " : ", no match.  ";
            if (matched)
            {
                t += "Show: " + me.Episode.TheSeries.Name + ", Season " + season + ", Ep " + epnum + ".  ";
                t += "To: " + me.TheFileNoExt;
            }

            LOGGER.Warn(t);
        }

        [NotNull]
        private static ItemMissing UpdateMissingItem([NotNull] ItemMissing me, [NotNull] FileInfo dce, int epF, int maxEp, int seasF)
        {
            if (me.Episode.Show is null)
            {
                return me;
            }
            ShowRule sr = new ShowRule
            {
                DoWhatNow = RuleAction.kMerge,
                First = epF,
                Second = maxEp
            };

            me.Episode.Show?.AddSeasonRule(seasF, sr);

            LOGGER.Info(
                $"Looking at {me.Episode.Show.ShowName} and have identified that episode {epF} and {maxEp} of season {seasF} have been merged into one file {dce.FullName}");

            LOGGER.Info($"Added new rule automatically for {sr}");

            //Regenerate the episodes with the new rule added
            ShowLibrary.GenerateEpisodeDict(me.Episode.Show);

            //Get the newly created processed episode we are after
            // ReSharper disable once InconsistentNaming
            ProcessedEpisode newPE = me.Episode;
            foreach (ProcessedEpisode pe in me.Episode.Show.SeasonEpisodes[seasF])
            {
                if (pe.AppropriateEpNum == epF && pe.EpNum2 == maxEp)
                {
                    newPE = pe;
                }
            }

            return new ItemMissing(newPE, me.TargetFolder);
        }

        private static bool FindExistingActionFor([NotNull] ItemList addTo,[NotNull] FileSystemInfo fi)
        {
            foreach (ActionCopyMoveRename existingFileOperation in addTo.CopyMoveItems())
            {
                if (String.Compare(existingFileOperation.From.FullName, fi.FullName,
                        StringComparison.OrdinalIgnoreCase) == 0)
                {
                    return true;
                }
            }

            return false;
        }
        protected void KeepTogether([NotNull] ItemList actionlist, bool fromLibrary) {
            KeepTogether(actionlist, fromLibrary, (!MDoc.Args.Unattended) && (!MDoc.Args.Hide));
                }

        public static void KeepTogether([NotNull] ItemList actionlist, bool fromLibrary,bool showErrors)
        {
            // for each of the items in rcl, do the same copy/move if for other items with the same
            // base name, but different extensions
            ItemList extras = new ItemList();

            foreach (ActionCopyMoveRename action in actionlist.CopyMoveItems())
            {
                KeepTogetherForItem(actionlist, fromLibrary, action, extras,showErrors);
            }

            UpdateActionList(actionlist, extras);
        }

        private static void KeepTogetherForItem(ItemList actionlist, bool fromLibrary, [NotNull] ActionCopyMoveRename action, ItemList extras,bool showErrors)
        {
            try
            {
                DirectoryInfo sfdi = action.From.Directory;
                string basename = action.From.Name;
                int l = basename.Length;
                basename = basename.Substring(0, l - action.From.Extension.Length);

                string toname = action.To.Name;
                int l2 = toname.Length;
                toname = toname.Substring(0, l2 - action.To.Extension.Length);

                try
                {
                    FileInfo[] flist = sfdi.GetFiles(basename + ".*");
                    foreach (FileInfo fi in flist)
                    {
                        //check to see whether the file is one of the types we do/don't want to include
                        //If we are copying from outside the library we use the 'Keep Together' Logic
                        if (!fromLibrary && !TVSettings.Instance.KeepTogetherFilesWithType(fi.Extension))
                        {
                            continue;
                        }

                        //If we are with in the library we use the 'Other Extensions'
                        if (fromLibrary && !TVSettings.Instance.FileHasUsefulExtension(fi, true))
                        {
                            continue;
                        }

                        string newName = GetFilename(fi.Name, basename, toname);

                        ActionCopyMoveRename newitem = new ActionCopyMoveRename(action.Operation, fi,
                            FileHelper.FileInFolder(action.To.Directory, newName), action.Episode, false,
                            null); // tidy up on main action, not this

                        // check this item isn't already in our to-do list
                        if (ActionListContains(actionlist, newitem))
                        {
                            continue;
                        }

                        if (!newitem.SameAs(action)) // don't re-add ourself
                        {
                            extras.Add(newitem);
                        }
                    }
                }
                catch (UnauthorizedAccessException)
                {
                    LOGGER.Warn("Could not access: " + action.From.FullName);
                }
                catch (DirectoryNotFoundException)
                {
                    LOGGER.Warn("Could not find: " + action.From.FullName);
                }
            }
            catch (PathTooLongException e)
            {
                string t = "Path or filename too long. " + action.From.FullName + ", " + e.Message;
                LOGGER.Warn(e, "Path or filename too long. " + action.From.FullName);

                if (showErrors && Environment.UserInteractive)
                {
                    MessageBox.Show(t, "Path or filename too long", MessageBoxButton.OK, MessageBoxImage.Exclamation);
                }
            }
        }

        [NotNull]
        private static string GetFilename([NotNull] string filename, [NotNull] string basename, string toname)
        {
            // do case insensitive replace
            int p = filename.IndexOf(basename, StringComparison.OrdinalIgnoreCase);

            string newName = filename.Substring(0, p) + toname + filename.Substring(p + basename.Length);
            if ((TVSettings.Instance.RenameTxtToSub) && newName.EndsWith(".txt", StringComparison.Ordinal))
            {
                return newName.Substring(0, newName.Length - 4) + ".sub";
            }

            return newName;
        }

        private static void UpdateActionList(ItemList actionlist, [NotNull] ItemList extras)
        {
            foreach (Item action in extras)
            {
                // check we don't already have this in our list and, if we don't add it!
                bool have = AlreadyHaveAction(actionlist, action);

                if (!have)
                {
                    actionlist.Insert(0, action); // put before other actions, so tidyup is run last
                }
            }
        }

        protected static void ReorganiseToLeaveOriginals([NotNull] ItemList newList)
        {
            // go through and change last of each operation on a given source file to a 'Move'
            // ideally do that move within same filesystem

            // sort based on source file, and destination drive, putting last if destdrive == sourcedrive
            newList.Sort(new ActionItemSorter());

            // sort puts all the CopyMoveRenames together				
            // then set the last of each source file to be a move
            for (int i = 0; i < newList.Count; i++)
            {
                ActionCopyMoveRename cmr1 = newList[i] as ActionCopyMoveRename;
                bool ok1 = cmr1 != null;

                if (!ok1)
                {
                    continue;
                }

                bool last = i == (newList.Count - 1);
                ActionCopyMoveRename cmr2 = !last ? newList[i + 1] as ActionCopyMoveRename : null;
                bool ok2 = cmr2 != null;

                if (ok2)
                {
                    ActionCopyMoveRename a1 = cmr1;
                    ActionCopyMoveRename a2 = cmr2;
                    if (!FileHelper.Same(a1.From, a2.From))
                    {
                        a1.Operation = ActionCopyMoveRename.Op.move;
                    }
                }
                else
                {
                    // last item, or last copymoverename item in the list
                    ActionCopyMoveRename a1 = cmr1;
                    a1.Operation = ActionCopyMoveRename.Op.move;
                }
            }
        }

        private static bool AlreadyHaveAction([NotNull] ItemList actionlist, Item action)
        {
            foreach (Item action2 in actionlist)
            {
                if (action2.SameAs(action))
                {
                    return true;
                }

                if ((action is Action a1) && (action2 is Action a2))
                {
                    if (a2.Produces == a1.Produces)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static bool ActionListContains([NotNull] ItemList actionlist, ActionCopyMoveRename newitem)
        {
            return actionlist.CopyMoveItems().Any(cmAction => cmAction.SameSource(newitem));
        }

        protected void ProcessMissingItem(TVDoc.ScanSettings settings, ItemList newList, ItemList toRemove, ItemMissing me, ItemList thisRound, [NotNull] List<FileInfo> matchedFiles,bool useFullPath)
        {
            if (matchedFiles.Count == 1)
            {
                if (!OtherActionsMatch(matchedFiles[0], me, settings,useFullPath))
                {
                    if (!FinderHelper.BetterShowsMatch(matchedFiles[0], me.Episode.Show,useFullPath,MDoc))
                    {
                        toRemove.Add(me);
                        newList.AddRange(thisRound);
                    }
                    else
                    {
                        LOGGER.Warn($"Ignoring potential match for {me.Episode.Show.ShowName} S{me.Episode.AppropriateSeasonNumber} E{me.Episode.AppropriateEpNum}: with file {matchedFiles[0]?.FullName} as there are multiple shows that match for that file");
                        me.AddComment(
                            $"Ignoring potential match with file {matchedFiles[0]?.FullName} as there are multiple shows for that file");
                    }
                }
                else
                {
                    LOGGER.Warn($"Ignoring potential match for {me.Episode.Show.ShowName} S{me.Episode.AppropriateSeasonNumber} E{me.Episode.AppropriateEpNum}: with file {matchedFiles[0]?.FullName} as there are multiple actions for that file");
                    me.AddComment(
                        $"Ignoring potential match with file {matchedFiles[0]?.FullName} as there are multiple actions for that file");
                }
            }

            if (matchedFiles.Count > 1)
            {
                List<FileInfo> bestMatchedFiles = IdentifyBestMatches(matchedFiles);

                if (bestMatchedFiles.Count == 1)
                {
                    //We have one file that is better, lets use it
                    toRemove.Add(me);
                    newList.AddRange(thisRound);
                }
                else
                {
                    foreach (FileInfo matchedFile in matchedFiles)
                    {
                        LOGGER.Warn(
                            $"Ignoring potential match for {me.Episode.Show.ShowName} S{me.Episode.AppropriateSeasonNumber} E{me.Episode.AppropriateEpNum}: with file {matchedFile?.FullName} as there are multiple files for that action");

                        me.AddComment(
                            $"Ignoring potential match with file {matchedFile?.FullName} as there are multiple files for that action");
                    }
                }
            }
        }

        [NotNull]
        private static List<FileInfo> IdentifyBestMatches([NotNull] List<FileInfo> matchedFiles)
        {
            //See whether there are any of the matched files that stand out
            List<FileInfo> bestMatchedFiles = new List<FileInfo>();
            foreach (FileInfo matchedFile in matchedFiles)
            {
                //test first file against all the others
                bool betterThanAllTheRest = true;
                foreach (FileInfo compareAgainst in matchedFiles)
                {
                    if (matchedFile.FullName == compareAgainst.FullName)
                    {
                        continue;
                    }

                    if (FileHelper.BetterQualityFile(matchedFile, compareAgainst) !=
                        FileHelper.VideoComparison.firstFileBetter)
                    {
                        betterThanAllTheRest = false;
                    }
                }
                if (betterThanAllTheRest)
                {
                    bestMatchedFiles.Add(matchedFile);
                }
            }

            return bestMatchedFiles;
        }

        private bool OtherActionsMatch(FileInfo matchedFile, Item me, TVDoc.ScanSettings settings,bool useFullPath)
        //This is used to check whether the selected file may match any other files we are looking for
        {
            foreach (ItemMissing testMissingAction in ActionList.MissingItems().ToList())
            {
                if (testMissingAction.SameAs(me))
                {
                    continue;
                }

                if (ReviewFile(testMissingAction, new ItemList(), matchedFile, settings,false,false,false,useFullPath))
                {
                    //We have 2 options that match  me and testAction - See whether one is subset of the other
                    if (me.Episode.Show.ShowName.Contains(testMissingAction.Episode.Show.ShowName))
                    {
                        continue; //'me' is a better match, so don't worry about the new one
                    }

                    return true;
                }
            }
            return false;
        }
    }
}
