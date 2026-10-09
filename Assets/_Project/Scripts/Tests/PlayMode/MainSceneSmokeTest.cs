using System.Collections;
using EpochsUnbound.Simulation;
using EpochsUnbound.WorldGen;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace EpochsUnbound.Tests
{
    public class MainSceneSmokeTest
    {
        [UnityTest]
        public IEnumerator MainSceneStreamsChunksAndTicks()
        {
            SceneManager.LoadScene("Main");
            yield return null;

            var group = Unity.Entities.World.DefaultGameObjectInjectionWorld.GetExistingSystemManaged<SimTickSystemGroup>();
            Assert.NotNull(group, "SimTickSystemGroup missing from default world");

            float end = Time.realtimeSinceStartup + 2f;
            while (Time.realtimeSinceStartup < end) yield return null;

            var streamer = Object.FindAnyObjectByType<ChunkStreamer>();
            Assert.Greater(streamer.LoadedCount, 20, "terrain chunks did not stream in");
            Assert.Greater(group.Clock.Tick, 10, "simulation clock did not tick");
        }
    }
}
