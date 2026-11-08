using System.Collections.Generic;
using UnityEngine;

namespace AiKodex.StadiumCreator2
{
    /// <summary>
    /// Simple seat source: a grid of standing/sitting spots on the ground. Useful for previewing the
    /// character library and animations, or for crowds without seats (concerts, fan zones, pitch invasions...).
    /// </summary>
    public class CrowdLineupSource : MonoBehaviour, ICrowdSeatSource
    {
        public int columns = 8;
        public int rows = 1;
        public float spacing = 0.9f;
        public float rowSpacing = 1.2f;
        public float standingBias = 0f;
        public int team = -1;

        public void CollectSeats(List<CrowdSeat> seats)
        {
            for (int r = 0; r < rows; r++)
            for (int c = 0; c < columns; c++)
            {
                var local = new Vector3((c - (columns - 1) * 0.5f) * spacing, 0, r * rowSpacing);
                seats.Add(new CrowdSeat
                {
                    position = transform.TransformPoint(local),
                    forward = -transform.forward,
                    section = 0, row = r, standingBias = standingBias, team = team,
                });
            }
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.cyan;
            var seats = new List<CrowdSeat>();
            CollectSeats(seats);
            foreach (var s in seats) Gizmos.DrawWireCube(s.position + Vector3.up * 0.9f, new Vector3(0.5f, 1.8f, 0.3f));
        }
    }
}
