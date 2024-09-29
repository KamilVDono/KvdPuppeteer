using System;
using Unity.Burst;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using Random = Unity.Mathematics.Random;

namespace Benchmarks
{
	[BurstCompile]
	public class SpawnSubjects : MonoBehaviour
	{
		[SerializeField, Range(1, 1_000_000)] uint _count = 1_000;
		[SerializeField, Range(10, 2_000)] uint _radius = 100;
		[SerializeField] GameObject[] _subjects;

		int _index = -1;
		GameObject _spawnedParent;
		AsyncInstantiateOperation<GameObject> _asyncInstantiateOperation;

		void Start()
		{
			UpdateSpawns();
		}

		void Update()
		{
			if (!Input.GetKeyDown(KeyCode.Space))
			{
				return;
			}
			_index = (_index + 1) == _subjects.Length ? -1 : _index + 1;
			UpdateSpawns();
		}

		void OnGUI()
		{
			var currentPrefabname = _index < 0 ? "None" : _subjects[_index].name;
			GUILayout.Label($"Current prefab: {currentPrefabname}; index: {_index}");
		}

		unsafe void UpdateSpawns()
		{
			if (_spawnedParent)
			{
				_asyncInstantiateOperation?.Cancel();
				Destroy(_spawnedParent);
			}

			if (_index < 0)
			{
				return;
			}

			_spawnedParent = new GameObject("Spawned parent");
			var rng = new Random(69);

			var positions = new Vector3[_count];
			var rotations = new Quaternion[_count];

			fixed (Vector3* positionsPtr = &positions[0])
			fixed (Quaternion* rotationsPtr = &rotations[0])
			{
				new FillSpawnDataJob
					{
						positions = positionsPtr,
						rotations = rotationsPtr,
						radius = _radius,
						rng = rng
					}.ScheduleParallel((int)_count, 128, default)
					.Complete();
			}

			var positionsSpan = new ReadOnlySpan<Vector3>(positions);
			var rotationsSpan = new ReadOnlySpan<Quaternion>(rotations);
			InstantiateAsync(_subjects[_index], (int)_count, _spawnedParent.transform, positionsSpan, rotationsSpan);
		}

		[BurstCompile]
		unsafe struct FillSpawnDataJob : IJobFor
		{
			[NativeDisableUnsafePtrRestriction] public Vector3* positions;
			[NativeDisableUnsafePtrRestriction] public Quaternion* rotations;
			public uint radius;
			public Random rng;

			public void Execute(int index)
			{
				var phi = rng.NextFloat(0, 2 * math.PI);
				var theta = rng.NextFloat(0, math.PI);
				var u = rng.NextFloat(0, 1);
				var r = radius * math.pow(u, 0.333f);
				var x = r * math.sin(theta) * math.cos(phi);
				var y = r * math.sin(theta) * math.sin(phi);
				var z = r * math.cos(theta);
				positions[index] = new Vector3(x, y, z);
				rotations[index] = rng.NextQuaternionRotation();
			}
		}
	}
}
