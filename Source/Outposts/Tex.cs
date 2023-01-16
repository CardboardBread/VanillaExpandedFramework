using UnityEngine;
using Verse;

namespace Outposts
{
    [StaticConstructorOnStartup]
    public static class Tex
    {
        public static readonly Texture2D PackTex = ContentFinder<Texture2D>.Get("UI/Gizmo/AbandonOutpost");
        public static readonly Texture2D AddTex = ContentFinder<Texture2D>.Get("UI/Gizmo/AddToOutpost");
        public static readonly Texture2D RemoveTex = ContentFinder<Texture2D>.Get("UI/Gizmo/RemovePawnFromOutpost");
        public static readonly Texture2D StopPackTex = ContentFinder<Texture2D>.Get("UI/Gizmo/CancelAbandonOutpost");
        public static readonly Texture2D RemoveItemsTex = ContentFinder<Texture2D>.Get("UI/Gizmo/RemoveItemsFromOutpost");
        public static readonly Texture2D CreateTex = ContentFinder<Texture2D>.Get("UI/Gizmo/SetUpOutpost");
    }
}