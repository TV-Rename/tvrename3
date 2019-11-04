using JetBrains.Annotations;

namespace TVRename
{
    internal class FindMissingEpisodesLocally : FindMissingEpisodes
    {
        public FindMissingEpisodesLocally(TVDoc doc) : base(doc) {}
        [NotNull]
        protected override string Checkname() => "Looked local filesystem for the missing files";
        protected override Finder.FinderDisplayType CurrentType() => Finder.FinderDisplayType.local;
    }
}
