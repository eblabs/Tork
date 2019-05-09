using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

using System;

namespace Adrenak.Tork {
	public class Wheel : MonoBehaviour {


        // get some info from the user
        public BasicWheelConfig wheelConfig;

        //[Tooltip("The radius of the wheel")]
        /// <summary>
        /// The radius of the wheel
        /// </summary>
        //public float radius = 0.5f;

        [Header("Spring")]
		[Tooltip("How far the spring expands when it is in air.")]
		/// <summary>
		/// How far the spring expands when it is in air.
		/// </summary>
		public float suspensionDistance = .2f;

		[Tooltip(
			"The constant in the Hooke's spring law." +
			" High values make the spring hard to compress" +
			" Make this higher for heavier vehicles"
		)]
		/// <summary>
		/// The k in the Hooke's spring law.
		/// High values make the spring hard to compress
		/// Make this higher for heavier vehicles
		/// </summary>
		public float stiffness = 10000;

		[Tooltip("Damping applied to the wheel. Lower values make the wheel bounce more.")]
		/// <summary>
		/// Damping applied to the wheel. 
		/// Lower values make the wheel bounce more.
		/// </summary>
		public float damping = 5000;

		[Tooltip("The rate (m/s) at which the spring relaxes to maximum length when the wheel is not on the ground.")]
		/// <summary>
		/// The rate (m/s) at which the spring relaxes to maximum length when the wheel is not on the ground.
		/// </summary>
		public float relaxRate = .5f;

		[Header("Friction")]
		[Tooltip(
			"Multiplier for the wheels sideways friction." +
			" Values below 1 will cause the wheel to drift" +
			" Values above 1 will cause sharp turns"
		)]
		/// <summary>
		/// Multiplier for the wheels sideways friction.
		/// Values below 1 will cause the wheel to drift
		/// Values above 1 will cause sharp turns
		/// </summary>
		public float sidewaysGrip = 1;

		[Tooltip(
			"Multiplier for the wheels forward friction." +
			" Values below 1 will cause the wheel to slip (like ice)" +
			" Values above 1 will cause the wheel to have high traction (and this higher speed)"
		)]
		/// <summary>
		/// Multiplier for the wheels forward friction.
		/// Values below 1 will cause the wheel to slip (like ice)
		/// Values above 1 will cause the wheel to have high traction (and this higher speed)
		/// </summary>
		public float forwardGrip = 1;

		[Tooltip(
			"Multiplier for the wheels sideways friction. " +
			" Values below 1 cause the wheel to skid" +
			" Values above 1 will cause the wheel to take sharper turns"
		)]
		/// <summary>
		/// Multiplier for the wheels sideways friction
		/// Values below 1 cause the wheel to skid
		/// Values above 1 will cause the wheel to take sharper turns
		/// </summary>
		public float rollingFriction = .1f;

		[Header("Collision")]
		[Tooltip(
			"Whether the normal force from the wheel collision should be faked. " +
			" True causes the force to be applied only along the wheels upward direction" +
			" causing causing it to not slow down from collisions"
		)]
		/// <summary>
		/// Whether the normal force from the wheel collision should be faked.
		/// True causes the force to be applied only along the wheels upward direction
		/// causing causing it to not slow down from collisions
		/// </summary>
		public bool fakeNormals;

		/// <summary>
		/// The layers used for wheel raycast
		/// </summary>
		//public LayerMask m_RaycastLayers;

        /// <summary>
        /// The velocity of the wheel (at the raycast hit point) in world space
        /// </summary>
        public Vector3 Velocity;

        /// <summary>
        /// The angle by which the wheel is turning
        /// </summary>
        public float steerAngle;

        /// <summary>
        /// The torque applied to the wheel for moving in the forward and backward direction
        /// </summary>
        public float motorTorque;

        /// <summary>
        /// The torque the brake is applying on the wheel
        /// </summary>
        public float brakeTorque;

        /// <summary>
        /// Whether the wheel is touching the ground
        /// </summary>
        public bool isGrounded;

        /// <summary>
        /// The distance to which the spring of the wheel is compressed
        /// </summary>
        public float CompressionDistance;
		float m_PrevCompressionDist;

        /// <summary>
        /// The ratio of compression distance and suspension distance
        /// 0 when the wheel is entirely uncompressed, 
        /// 1 when the wheel is entirely compressed
        /// </summary>
        public float CompressionRatio;

        /// <summary>
        /// The raycast hit point of the wheel
        /// </summary>
        //public RaycastHit Hit;
		//RaycastHit m_Hit;

        /// <summary>
        /// The force the spring exerts on the rigidbody
        /// </summary>
        public Vector3 SpringForce;

		Ray m_Ray;
		new Rigidbody rigidbody;
		const float k_RayStartHeight = 1;

        private WheelCast wheelcast;
        private Transform suspensionAnchor;

        [Serializable]
        public struct InternalData
        {
            public float local_hit_distance;
            public Vector3 local_hit_point;
            public float hit_radius;
            public float full_hit_distance;
            public float full_ray_length;
            public float suspension_distance;
            public float fakedScale;
            public float localUpHitNormalDot;
            public Vector3 forceDirection;
            public Vector3 latForce;
            public Vector3 longForce;
            public Vector3 momentum;
            public Vector3 velocity;
        }

        // for internal data
        public InternalData internalData;

        public WheelCast.HitData active_hit_data;

        void Start() {
			m_Ray = new Ray();

			// Remove rigidbody component from the wheel
			rigidbody = GetComponent<Rigidbody>();
			if (rigidbody)
				Destroy(rigidbody);

			// Get the rigidbody component from the parent
			rigidbody = GetComponentInParent<Rigidbody>();

            // add wheelcast child object
            GameObject tmp;
            tmp = new GameObject("wheelcast");
            tmp.transform.SetParent(this.transform);
            wheelcast = tmp.AddComponent<WheelCast>();
            wheelcast.wheelConfig = wheelConfig;
            wheelcast.PrepWheelcastRayData(this.transform, wheelConfig.wheelRadius, suspensionDistance, k_RayStartHeight);

            // suspension anchor 
            suspensionAnchor = new GameObject("Anchor").transform;
            suspensionAnchor.transform.SetParent(this.transform);
            suspensionAnchor.position = this.transform.position + (this.transform.up * k_RayStartHeight);

        }

		void FixedUpdate() {
			transform.localEulerAngles = new Vector3(0, steerAngle, 0);

            SetupWheelcast();
            CalculateSuspension();
			CalculateFriction();
		}

        public void SetupWheelcast() {


            // align wheelcast to axle position
            internalData.suspension_distance = (GetRayLen() - k_RayStartHeight - CompressionDistance - wheelConfig.wheelRadius);
            wheelcast.transform.position = transform.TransformPoint(-Vector3.up * internalData.suspension_distance);
            wheelcast.ProcessRays();

            // pass data along
            //active_hit_data = wheelcast.shortest_output;
            active_hit_data = wheelcast.average_output;

            // calculate hit distance in suspension space
            // get suspension axis aligned radius for hit point
            internalData.local_hit_point = wheelcast.transform.InverseTransformPoint(active_hit_data.point);
            internalData.local_hit_distance = Mathf.Abs(internalData.local_hit_point.y);
            internalData.hit_radius = Mathf.Abs((internalData.local_hit_point.normalized * wheelConfig.wheelRadius).y);
            internalData.full_hit_distance = Mathf.Abs(suspensionAnchor.transform.InverseTransformPoint(active_hit_data.point).y);
            internalData.full_ray_length = suspensionDistance + internalData.hit_radius + k_RayStartHeight;

            // for situation when top of wheel is hit
            // todo, //if (internalData.local_hit_point.y>0)
        }

        void CalculateSuspension() {
            // If we did not hit, relax the spring and return
            if (!active_hit_data.hasHit)
            {
                m_PrevCompressionDist = CompressionDistance;
                CompressionDistance = CompressionDistance - Time.fixedDeltaTime * relaxRate;
                CompressionDistance = Mathf.Clamp(CompressionDistance, 0, suspensionDistance);
                CompressionRatio = Mathf.Clamp01(CompressionDistance / suspensionDistance);
                isGrounded = false;
                return;
            }

            var force = 0.0f;
            isGrounded = true;
            internalData.localUpHitNormalDot = Vector3.Dot(active_hit_data.normal, transform.up);
            internalData.fakedScale = internalData.localUpHitNormalDot * Mathf.Sign(internalData.localUpHitNormalDot);

            // calculate compression distance and ratio
            CompressionDistance = internalData.full_ray_length - internalData.full_hit_distance;
            CompressionRatio = Mathf.Clamp01( CompressionDistance / suspensionDistance); // if we get overcompressed, allow the compression ratio to go beyond 1 for this moment
            CompressionDistance = Mathf.Clamp(CompressionDistance, 0, suspensionDistance);

            // Calculate the force from the springs compression using Hooke's Law
            float springForce = stiffness * CompressionRatio;
            force += springForce;

            // Calculate the damping force based on compression rate of the spring
            float rate = (CompressionDistance - m_PrevCompressionDist) / Time.fixedDeltaTime;
            m_PrevCompressionDist = CompressionDistance;

            float damperForce = rate * damping;
            force += damperForce;

            // When normals are faked, the spring force vector is not applied towards the wheel's center,
            // instead it is resolved along the global Y axis and applied
            // This helps maintain velocity over speed bumps, however may be unrealistic
            if (fakeNormals)
                SpringForce = Vector3.up * force;
            else
            {
                /*
                float fakedScale = Vector3.Dot(Hit.normal, transform.up);
				force *= fakedScale;
				SpringForce = transform.up * force;
                */
                internalData.forceDirection = Vector3.Lerp(Vector3.up, transform.up, Mathf.Clamp01(internalData.fakedScale));
                SpringForce = internalData.forceDirection * force;
            }

            // Apply suspension force
            Debug.DrawRay(active_hit_data.point, SpringForce, Color.red);
            rigidbody.AddForceAtPosition(SpringForce, (active_hit_data.point));
		}

		void CalculateFriction() {
            // opt out
            if (!active_hit_data.hasHit) return;

            // sample velocity
            internalData.velocity = rigidbody.GetPointVelocity(active_hit_data.point);

            // Contact basis (can be different from wheel basis)
            Vector3 normal = active_hit_data.normal;// Hit.normal;
            Vector3 side = transform.right;
            //side.y = 0;
            Vector3 forward = transform.forward;
            //forward.y = 0;

            // Apply less force if the vehicle is tilted
            var angle = Vector3.Angle(normal, transform.up);
            float multiplier = 1f;// Mathf.Abs(Vector3.Dot(normal, transform.up)); //1f;//Mathf.Cos(angle * Mathf.Deg2Rad);

            // Calculate sliding velocity (velocity without normal force)
            Vector3 sideVel = Vector3.Dot(Velocity, side) * side * multiplier;
            Vector3 forwardVel = Vector3.Dot(Velocity, forward) * forward * multiplier;
            Vector3 velocity2D = sideVel + forwardVel;

            // contact forward normal
            internalData.momentum = velocity2D;// * rigidbody.mass;

            internalData.latForce = Vector3.Dot(-internalData.momentum, side) * side * sidewaysGrip;
            internalData.longForce = Vector3.Dot(-internalData.momentum, forward) * forward * forwardGrip;
            Vector3 frictionForce = Vector3.zero;

            frictionForce += internalData.longForce;
            frictionForce += internalData.latForce;

            // Apply rolling friction
            internalData.longForce *= 1 - rollingFriction;

            // Apply brake force
            var brakeForceMag = brakeTorque / wheelConfig.wheelRadius;
            brakeForceMag = Mathf.Clamp(brakeForceMag, 0, internalData.longForce.magnitude);
            internalData.longForce -= internalData.longForce.normalized * brakeForceMag;
            frictionForce -= internalData.longForce;

            Debug.DrawRay(active_hit_data.point, frictionForce, Color.blue);
            rigidbody.AddForceAtPosition(frictionForce * rigidbody.mass, active_hit_data.point); //Hit.point

            Debug.DrawRay(active_hit_data.point, forward * motorTorque / wheelConfig.wheelRadius * forwardGrip, Color.magenta);
            rigidbody.AddForceAtPosition(forward * motorTorque / wheelConfig.wheelRadius * forwardGrip, active_hit_data.point);//Hit.point

		}

		float GetRayLen() {
			return suspensionDistance + wheelConfig.wheelRadius + k_RayStartHeight;
		}

	#if UNITY_EDITOR
		void OnDrawGizmos() {
			Handles.color = Color.yellow;
			Handles.DrawWireDisc(transform.position, transform.right, wheelConfig.wheelRadius);

			Handles.color = Color.red;
			var p1 = transform.position + transform.up * k_RayStartHeight;
			var p2 = transform.position - transform.up * (GetRayLen() - k_RayStartHeight);
			Handles.DrawLine(p1, p2);

			var pos = transform.position + (-transform.up * (GetRayLen() - k_RayStartHeight - CompressionDistance - wheelConfig.wheelRadius));
			Handles.DrawWireDisc(pos, transform.right, wheelConfig.wheelRadius);
		}
#endif

        [Serializable]
        public struct BasicWheelConfig
        {
            [Header("WheelSetup")]
            public bool enable3DRaycasts;
            public float wheelRadius;
            public float wheelWidth;

            [Header("Raycasts")]
            public int rayCountRadial;
            public int rayCountCrossSection;
            public float span;

            [HideInInspector]
            public int raycastCount;

            [Header("Layers")]
            public LayerMask layer_mask;
        }

    }
}
