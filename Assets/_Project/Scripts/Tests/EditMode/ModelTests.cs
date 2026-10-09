using EpochsUnbound.Settlement;
using NUnit.Framework;
using UnityEngine;

namespace EpochsUnbound.Tests
{
    public class ModelTests
    {
        [Test]
        public void BuildingModelsFitTheirFootprintAndStandOnTheGround()
        {
            var settings = ScriptableObject.CreateInstance<SettlementSettings>();
            foreach (var def in settings.Buildings)
            {
                var mesh = Models.Building(def);
                var b = mesh.bounds;
                Assert.Greater(mesh.triangles.Length, 30, def.Name);
                Assert.LessOrEqual(b.extents.x, def.Size.x * 0.5f + 0.05f, $"{def.Name} wider than its footprint");
                Assert.LessOrEqual(b.extents.z, def.Size.z * 0.5f + 0.05f, $"{def.Name} deeper than its footprint");
                Assert.AreEqual(0f, b.min.y, 0.01f, $"{def.Name} should stand on y = 0");
                Object.DestroyImmediate(mesh);
            }
            Object.DestroyImmediate(settings);
        }

        [Test]
        public void CitizenIsAboutHumanSize()
        {
            var mesh = Models.Citizen(Models.ShirtBlue);
            Assert.AreEqual(1.72f, mesh.bounds.size.y, 0.05f);
            Assert.AreEqual(0f, mesh.bounds.min.y, 0.01f);
            Assert.Less(mesh.bounds.size.x, 0.7f);
            Object.DestroyImmediate(mesh);
        }
    }
}
