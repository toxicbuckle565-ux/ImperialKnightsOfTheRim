using Verse;

namespace LIK
{
    /// <summary>
    /// The 'immortal colonists' TESTING option moved out of the mod on 2026-11-23 into the separate local mod Knights Test Tools
    /// (Mods/LocalKnightsTestTools, never released). This empty map component stays so saves that already contain it load without errors.
    /// </summary>
    public class MapComponent_TestImmortal : MapComponent
    {
        public MapComponent_TestImmortal(Map map) : base(map) { }
    }
}
