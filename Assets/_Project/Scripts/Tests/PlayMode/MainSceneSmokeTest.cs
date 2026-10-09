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

            // Warm up first (Burst compiles new jobs in the background on a cold cache), then measure.
            float end = Time.realtimeSinceStartup + 3f;
            while (Time.realtimeSinceStartup < end) yield return null;
            long tick0 = group.Clock.Tick;
            end = Time.realtimeSinceStartup + 2f;
            while (Time.realtimeSinceStartup < end) yield return null;

            var streamer = Object.FindAnyObjectByType<ChunkStreamer>();
            Assert.Greater(streamer.LoadedCount, 20, "terrain chunks did not stream in");
            Assert.Greater(group.Clock.Tick - tick0, 30, "simulation clock did not tick at ~20 Hz");
        }
    }
}
