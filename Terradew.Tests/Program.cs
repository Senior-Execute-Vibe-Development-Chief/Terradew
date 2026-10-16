if (args.Length > 0 && args[0] == "check")
    return NpcContentTests.CheckOnly(args.Skip(1));

int failed = 0;
failed += ProtocolTests.Run();
failed += SpriteTests.Run();
failed += WorldLayoutTests.Run();
failed += FishTableTests.Run();
failed += TownRulesTests.Run();
failed += TownHomesTests.Run();
failed += RoutePlannerTests.Run();
failed += EconomyClockTests.Run();
failed += WeatherTests.Run();
failed += PlacesTests.Run();
failed += GridPathTests.Run();
failed += MiningTests.Run();
failed += WallClimbTests.Run();
failed += CelestialTests.Run();
failed += DyeTintTests.Run();
failed += NpcContentTests.Run();
failed += CaveTests.Run();
failed += WorldLightTests.Run();
failed += BossBarTests.Run();
failed += QuarryTests.Run();
failed += FootingTests.Run();
Console.WriteLine(failed == 0 ? "all passed" : $"{failed} failed");
return failed == 0 ? 0 : 1;
