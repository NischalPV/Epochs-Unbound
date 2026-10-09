using System.Collections;
using EpochsUnbound.Settlement;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace EpochsUnbound.Tests
{
    /// <summary>Drives the Main scene through simulated mouse and keyboard, the way a player would.</summary>
    public class PlayerInputTests : InputTestFixture
    {
        [UnityTest]
        public IEnumerator ClickPlacesTownCentreAndKeysStartPlacement()
        {
            var mouse = InputSystem.AddDevice<Mouse>();
            var keyboard = InputSystem.AddDevice<Keyboard>();
            SceneManager.LoadScene("Main");
            float end = Time.realtimeSinceStartup + 3f;
            while (Time.realtimeSinceStartup < end) yield return null;

            var controller = Object.FindAnyObjectByType<SettlementController>();
            Assert.AreEqual(BuildingKind.TownCentre, controller.Placing, "game should open in Town Centre placement");

            Set(mouse.position, new Vector2(Screen.width / 2f, Screen.height / 2f));
            yield return null;
            yield return null;
            Debug.Log($"[Input] before click: {controller.Message}");
            Press(mouse.leftButton);
            yield return null;
            Release(mouse.leftButton);
            end = Time.realtimeSinceStartup + 0.5f; // population is recounted on the next sim tick
            while (Time.realtimeSinceStartup < end) yield return null;

            var em = Unity.Entities.World.DefaultGameObjectInjectionWorld.EntityManager;
            Assert.AreEqual(controller.Settings.StartingCitizens, SettlementOps.GetColony(em).Population, "click at screen centre should build the Town Centre: " + controller.Message);
            Assert.IsNull(controller.Placing);

            PressAndRelease(keyboard.fKey);
            yield return null;
            Assert.AreEqual(BuildingKind.Farm, controller.Placing, "F should start farm placement");
        }
    }
}
