using BrilliantSkies.Blocks.BreadBoards;
using BrilliantSkies.Core.ImageFinder;
using BrilliantSkies.Effects.SoundSystem;
using BrilliantSkies.Effects.SoundSystem.Internal;
using HarmonyLib;
using UnityEngine;

namespace FTDFilmTheDepthsMod
{
    /// <summary>
    /// Wrapper class to mark request as being from a BreadBoard.
    /// </summary>
    public class SoundRequestBreadBoard : SoundRequest
    {
        public SoundRequestBreadBoard(SoundRequest sr)
            : base(sr.ClipDefinition, sr.GameWorldPositionOfSound)
        {
        }
    }

    /// <summary>
    /// Tag SoundRequest as having been from a BreadBoard.
    /// </summary>
    // [HarmonyPatch(typeof(Playback), "Play")]
    class Playback_Play_Patch
    {
        public static bool overridePlayback = true;

        static bool Prefix(Playback __instance, MainConstruct ____construct, Playback.SoundPlayCallback ____playback, float volumeMod, float pitchMod)
        {
            if (!overridePlayback)
                return true;
            string us = __instance.HashLabel.Us;
            SoundRequest request = new SoundFinder().GetRequest(____construct.CentreOfMass, us);
            if (request != null)
            {
                request = new SoundRequestBreadBoard(request)
                {
                    MinDistance = volumeMod * __instance.Volume.RndBetween,
                    Pitch = pitchMod * __instance.Pitch.RndBetween,
                    DelayInRequest = __instance.Delay.RndBetween
                };
                ____playback.Enqueue(request);
            }
            return false;
        }
    }

    /// <summary>
    /// Remove or reinstate doppler effect based on sound origin.
    /// </summary>
    // [HarmonyPatch(typeof(AdvSoundPlayer), "PlayOnceHere")]
    class AdvSoundPlayer_PlayOnceHere_Patch
    {
        static void Prefix(AudioSource ____audioSource, SoundRequestWrapper request)
        {
            ____audioSource.dopplerLevel = request.Request is SoundRequestBreadBoard ? 0f : 1f;
        }
    }
}
