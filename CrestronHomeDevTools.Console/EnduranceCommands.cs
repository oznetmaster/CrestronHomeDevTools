// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE in the repository root.

using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;

using CrestronHomeDevTools;

internal static class EnduranceCommands
	{
	private static readonly JsonSerializerOptions JsonOptions = new ()
		{ PropertyNameCaseInsensitive = true, WriteIndented = true, Converters = { new JsonStringEnumConverter () } };
	internal static SubmissionEnduranceWorkerPlan Read (string file)
		{
		if (new FileInfo (file).Length > 1024 * 1024)
			throw new ArgumentException ("The endurance worker plan exceeds its size limit.");
		var worker = JsonSerializer.Deserialize<SubmissionEnduranceWorkerPlan> (File.ReadAllBytes (file), JsonOptions)
			?? throw new ArgumentException ("The endurance worker plan is empty.");
		if (worker.Plan == null || worker.Processor == null || worker.Probe == null)
			throw new ArgumentException ("An endurance worker plan requires plan, processor and probe sections.");
		if (SubmissionEnduranceProcessProbe.GetProducerId (worker.Probe) != worker.Plan.ProducerId)
			throw new ArgumentException ("The endurance worker's producer does not match its pinned plan.");
		return worker;
		}
	internal static int Offline (string command, string directory, SubmissionEnduranceWorkerPlan worker)
		{
		var evidence = SubmissionEnduranceMonitor.GetEvidenceDirectory (directory);
		var status = SubmissionEnduranceMonitor.ReadStatus (directory, worker.Plan, worker.Processor);
		if (command == "endurance-export" && status.ReservationState != "Released")
			throw new InvalidOperationException ("Finish and verify monitor reservation cleanup before exporting from the CLI.");
		object? result = command == "endurance-export"
			? SubmissionEndurance.Export (evidence, worker.Plan, DateTimeOffset.UtcNow)
			: status;
		Console.WriteLine (JsonSerializer.Serialize (result, JsonOptions));
		if (status.ReservationState is "Acquiring" or "Releasing" || status.Checkpoint?.State is SubmissionEnduranceState.ProbePending or SubmissionEnduranceState.Interrupted) return 3;
		return status.Checkpoint?.State == SubmissionEnduranceState.Failed ? 1 : 0;
		}
	internal static async Task<int> RunAsync (string command, string directory, SubmissionEnduranceWorkerPlan worker,
		SubmissionEnduranceProcessor endpoint, NetworkCredential credential, CancellationToken token)
		{
		if (endpoint != worker.Processor)
			throw new ArgumentException ("Connection settings do not match the endurance plan's pinned processor and SSH identity.");
		if (command == "endurance-start")
			{
			SubmissionEnduranceProcessProbe.Validate (worker.Probe, worker.Plan);
			await SubmissionEnduranceMonitor.StartAsync (directory, worker.Plan, endpoint, credential, token);
			Console.WriteLine ("{\"state\":\"Held\",\"observationPerformed\":false}");
			return 0;
			}
		var status = SubmissionEnduranceMonitor.ReadStatus (directory, worker.Plan, endpoint);
		if (status.ReservationState is "Acquiring" or "Releasing" ||
			(command == "endurance-finish" && status.Checkpoint?.State is SubmissionEnduranceState.ProbePending or SubmissionEnduranceState.Interrupted))
			{
			Console.WriteLine (JsonSerializer.Serialize (status, JsonOptions));
			return 3;
			}
		if (command == "endurance-finish")
			{
			await SubmissionEnduranceMonitor.FinishAsync (directory, worker.Plan, endpoint, credential, token);
			Console.WriteLine ("{\"reservationReleased\":true}");
			return 0;
			}
		var checkpoint = await SubmissionEnduranceMonitor.CollectAsync (directory, worker.Plan, endpoint, credential,
			ct => SubmissionEnduranceProcessProbe.RunAsync (worker.Probe, worker.Plan, ct), token);
		bool released = false;
		if (checkpoint.State is SubmissionEnduranceState.Passed or SubmissionEnduranceState.Failed)
			{
			await SubmissionEnduranceMonitor.FinishAsync (directory, worker.Plan, endpoint, credential, token);
			released = true;
			}
		Console.WriteLine (JsonSerializer.Serialize (new { Checkpoint = checkpoint, ReservationReleased = released }, JsonOptions));
		return checkpoint.State switch
			{
				SubmissionEnduranceState.Failed => 1,
				SubmissionEnduranceState.Interrupted or SubmissionEnduranceState.ProbePending => 3,
				_ => 0
				};
		}
	}