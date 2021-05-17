using BrilliantSkies.Core.Returns.Rotation;
using UnityEngine;

namespace FTDFilmTheDepthsMod
{
    /// <summary>
    /// God forbid this needs to be used.
    /// </summary>
    public class RotationReturnForce : RotationReturnAbstract
    {
        public RotationReturnForce(Force f)
        {
            this._f = f;
        }

        public override bool Valid
        {
            get
            {
                return this._f != null && !this._f.Deleted;
            }
        }

        public override Quaternion Rotation
        {
            get
            {
                return this.Valid ? _f.GetGameRotationOfForce() : Quaternion.identity;
            }
        }

        private Force _f;
    }
}