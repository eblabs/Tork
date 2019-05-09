using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;

namespace Adrenak.Tork
{
    public class WheelCast : MonoBehaviour
    {
        // user defined stuff
        public Wheel.BasicWheelConfig wheelConfig;

        // hit data
        public HitData average_output;
        public HitData shortest_output;

        // rays
        public List<WheelcastRayData> wheelcastRays;

        // data for processing raycasts
        public RayProcesing rayProcessing;


        // rayprocessing cleanup
        private void OnDisable()
        {
            // cleanup
            OnDestroy();
        }

        private void OnDestroy()
        {
            // cleanup
            try
            {
                rayProcessing.raycast_commands.Dispose();
                rayProcessing.raycast_hits.Dispose();
            }
            catch { }
        }

        // define all of the rays used
        public void PrepWheelcastRayData(Transform wheelTransform, float radius, float suspensionDistance, float rayStartHeight = 1)
        {

            // wheelcast data
            wheelcastRays = new List<WheelcastRayData>();

            // main ray, fixed
            var tmpRay = new WheelcastRayData();
            tmpRay.parentTransform = wheelTransform;
            tmpRay.localPosition = Vector3.up * rayStartHeight;
            tmpRay.localDirection = Vector3.down;
            tmpRay.distance = radius + suspensionDistance + rayStartHeight;
            wheelcastRays.Add(tmpRay);


            /*
            // 2 extra auxilary rays
            tmpRay = new WheelcastRayData();
            tmpRay.parentTransform = wheelTransform;
            tmpRay.localPosition = Vector3.up * rayStartHeight + (-Vector3.right * input.wheelWidth * 0.5f) + (Vector3.right * 0);
            tmpRay.localDirection = Vector3.down;
            tmpRay.distance = radius + suspensionDistance + rayStartHeight;
            wheelcastRays.Add(tmpRay);

            tmpRay = new WheelcastRayData();
            tmpRay.parentTransform = wheelTransform;
            tmpRay.localPosition = Vector3.up * rayStartHeight + (-Vector3.right * input.wheelWidth * 0.5f) + (Vector3.right * input.wheelWidth);
            tmpRay.localDirection = Vector3.down;
            tmpRay.distance = radius + suspensionDistance + rayStartHeight;
            wheelcastRays.Add(tmpRay);
            */

            // toggle 3D raycasts
            if (wheelConfig.enable3DRaycasts)
            {
                // radial rays, dynamic, attached to moving wheel

                // define some steps
                float cStep = wheelConfig.wheelWidth / (wheelConfig.rayCountCrossSection - 1);
                float radianStep = (2 * Mathf.PI / wheelConfig.rayCountRadial);
                float angle;
                float x;
                float y;
                float z;

                // loop
                //int i = 1;
                for (int c = 0; c < wheelConfig.rayCountCrossSection; c++)
                {
                    for (int r = 0; r < wheelConfig.rayCountRadial; r++)
                    {
                        // create 
                        tmpRay = new WheelcastRayData();

                        // parent
                        tmpRay.parentTransform = this.transform;

                        // local center
                        tmpRay.localPosition = (-Vector3.right * wheelConfig.wheelWidth * 0.5f) + (Vector3.right * cStep * c);

                        // local direction
                        angle = radianStep * r;
                        x = 0f;
                        y = Mathf.Sin(angle);
                        z = Mathf.Cos(angle);
                        tmpRay.localDirection = new Vector3(x, y, z).normalized;

                        // distance
                        tmpRay.distance = radius;

                        // 
                        wheelcastRays.Add(tmpRay);
                    }
                }
            }

            // ray cast jobs
            rayProcessing.raycast_commands = new NativeArray<RaycastCommand>(wheelcastRays.Count, Allocator.Persistent);
            rayProcessing.raycast_hits = new NativeArray<RaycastHit>(wheelcastRays.Count, Allocator.Persistent);
        }



        public void ProcessRays()
        {

            // raycast jobs
            int raycast_count = wheelConfig.rayCountRadial * wheelConfig.rayCountCrossSection;

            // define some steps
            float cStep = wheelConfig.wheelWidth / (wheelConfig.rayCountCrossSection - 1);
            float rStep = 360 / (wheelConfig.rayCountRadial);
            float radianStep = 2 * Mathf.PI / wheelConfig.rayCountRadial;

            // process into world space
            for (int i = 0; i < wheelcastRays.Count; i++)
            {
                //
                var w = wheelcastRays[i];

                // center position
                rayProcessing.raycast_command.from = w.parentTransform.TransformPoint(w.localPosition);

                // direction
                w.direction = w.parentTransform.TransformDirection(w.localDirection);
                rayProcessing.raycast_command.direction = w.direction;

                // other params
                rayProcessing.raycast_command.distance = w.distance;
                rayProcessing.raycast_command.layerMask = wheelConfig.layer_mask;
                rayProcessing.raycast_command.maxHits = 1;

                // add to batch process
                rayProcessing.raycast_commands[i] = rayProcessing.raycast_command;

                //debugging
                Debug.DrawRay(rayProcessing.raycast_command.from, rayProcessing.raycast_command.direction.normalized * rayProcessing.raycast_command.distance, Color.blue);

                //store
                wheelcastRays[i] = w;
            }

            // process 
            rayProcessing.raycast_job_handle = RaycastCommand.ScheduleBatch(rayProcessing.raycast_commands, rayProcessing.raycast_hits, 10);
            rayProcessing.raycast_job_handle.Complete();

            // outputs
            average_output = NewHitData();
            shortest_output = NewHitData();


            float weight = 0f;
            float total_weight = 0f;
            int count = 0;
            float shortestDistance = 1000f;

            for (int i = 0; i < wheelcastRays.Count; i++)
            {
                // collect data
                var w = wheelcastRays[i];
                w.hitData = NewHitData();


                // no hit, skip
                rayProcessing.raycast_hit = rayProcessing.raycast_hits[i];
                if (!(rayProcessing.raycast_hit.distance > 0))
                {
                    continue;
                }

                // store data
                w.hitData.distance = rayProcessing.raycast_hit.distance;
                w.hitData.hasHit = true;
                w.hitData.normal = rayProcessing.raycast_hit.normal;
                w.hitData.point = rayProcessing.raycast_hit.point;
                w.hitData.direction = w.direction;

                // get weight
                weight = rayProcessing.raycast_hit.distance;
                //weight = Mathf.Pow(weight, 5);
                total_weight += weight;

                // shortest
                if (rayProcessing.raycast_hit.distance < shortestDistance)
                {
                    shortest_output = w.hitData;
                    shortestDistance = rayProcessing.raycast_hit.distance;
                }

                // average
                average_output.hasHit = true;
                average_output.point += rayProcessing.raycast_hit.point * weight;
                average_output.normal += rayProcessing.raycast_hit.normal * weight;
                average_output.distance += rayProcessing.raycast_hit.distance * weight;
                average_output.direction += w.hitData.direction * weight;

                //
                wheelcastRays[i] = w;

                //
                count++;
            }

            average_output.normal = average_output.hasHit ? Vector3.Normalize(average_output.normal) : Vector3.zero;
            average_output.point = average_output.hasHit ? average_output.point / total_weight : Vector3.zero;
            average_output.distance = average_output.hasHit ? average_output.distance / total_weight : 0f;
            average_output.direction = average_output.hasHit ? average_output.direction / total_weight : Vector3.zero;

            // debugggin
            Debug.DrawRay(shortest_output.point, shortest_output.normal * shortest_output.distance, Color.yellow, 0, false);
        }


        public HitData NewHitData()
        {
            var hitData = new HitData();
            hitData.point = Vector3.zero;
            hitData.normal = Vector3.zero;
            hitData.distance = 0;
            hitData.hasHit = false;
            return hitData;
        }

        [Serializable]
        public struct RayProcesing
        {
            public NativeArray<RaycastHit> raycast_hits;
            public RaycastHit raycast_hit;
            public NativeArray<RaycastCommand> raycast_commands;
            public RaycastCommand raycast_command;// = new RaycastCommand();
            public JobHandle raycast_job_handle;
        }

        [Serializable]
        public struct WheelcastRayData
        {
            // construction
            public Vector3 localPosition;
            public Vector3 localDirection;
            public Vector3 direction;
            public float distance;
            public Transform parentTransform;

            // hit data
            public HitData hitData;
        }

        [Serializable]
        public struct InputData
        {
            // wheel specs
            public float wheelRadius;
            public float wheelWidth;

            // ray casts
            public int rayCountRadial;
            public int rayCountCrossSection;
            public float span;

            [HideInInspector]
            public int raycastCount;

            //
            public bool enable3DRaycasts;

            // 
            public LayerMask layer_mask;
        }

        [Serializable]
        public struct HitData
        {
            public Vector3 point;
            public Vector3 normal;
            public Vector3 direction;
            public float distance;
            public bool hasHit;
        }
    }
}