using UnityEngine;

namespace RoomSiege.Visuals
{
    public static class WeaponArtwork
    {
        public static Texture2D Load(string weaponId, string skinId = null)
        {
            if (!string.IsNullOrEmpty(skinId))
            {
                var skin = Resources.Load<Texture2D>("Codex/Skins/Skin_" + skinId);
                if (skin != null) return skin;
            }
            return Resources.Load<Texture2D>("Codex/Towers/CodexTower_" + weaponId);
        }
    }
}
