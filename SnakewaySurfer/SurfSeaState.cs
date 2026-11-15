using UnityEngine;

namespace SnakewaySurfer;

// The sea mode's own state (architecture step 5): everything a crossing keeps, in one object the
// sea owns, instead of loose fields on the run. Board() starts a crossing, the landing and
// ResetSea() end it (SurfSea.cs). The values and the order they're set in are exactly the ones
// the loose fields had, so the crossing plays as before (playtesters loved it).
// The ship's own parts (m_ship, the sail and rudder transforms, sounds) stay with the ship.
internal sealed partial class SurfGame
{
	private sealed class SeaState
	{
		public int Phase;
		public float PhaseT;
		public Vector3 LeapFrom;
		public float LeapToZ;
		public bool AtSea;
		public float Heading;
		public float TurnRate;
		public Sail SailSet = Sail.Half;
		public float SailPos = 0.5f;
		public float BoatSpeed;
		public float WindAngle;
		public float WindTarget;
		public float WindStrength = 0.7f;
		public float WindStrengthTarget = 0.7f;
		public float WindTimer = 18f;
		public float WindShift;
		public float Surf;
		public float SeaTime;
		public float Storm;
		public float Scrape;
		public float BoatY;
		public float Pitch;
		public float Roll;
		public float Lurch;
		public float LurchV;
		public float Reload;
		public float DeckRun = 1f;
	}

	private readonly SeaState m_sea = new();
}
