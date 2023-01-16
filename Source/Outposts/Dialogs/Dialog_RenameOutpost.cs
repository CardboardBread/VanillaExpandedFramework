using Verse;

namespace Outposts
{
    public class Dialog_RenameOutpost : Dialog_Rename
    {
        private readonly Outpost outpost;

        public Dialog_RenameOutpost(Outpost outpost)
        {
            this.outpost = outpost;
            curName = outpost.Name;
        }

        protected override void SetName(string name)
        {
            outpost.Name = name;
        }
    }
}