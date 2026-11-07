using NUnit.Framework;
using UnityEngine;

namespace RoomSiege.UI.Tests
{
    public sealed class CodexAssetTests
    {
        [Test]
        public void CodexContainsRealEnemyAndTowerPreviewImages()
        {
            foreach(var id in new[]{"Grunt","Runner","Tank","Shielded","Boss"})
                Assert.IsNotNull(Resources.Load<Texture2D>("Codex/Enemies/CodexEnemy_"+id),"missing enemy image "+id);
            foreach(var id in new[]{"MG","Frost","Laser","Trap"})
                Assert.IsNotNull(Resources.Load<Texture2D>("Codex/Towers/CodexTower_"+id),"missing tower image "+id);
        }
    }
}
