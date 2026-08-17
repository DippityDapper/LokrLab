namespace LokrAbilityLab.Editor
{
	/// <summary>Creates a typed action card with sensible default field values.</summary>
	internal static class AbilityCardFactory
	{
		internal static ActionCard Create(string typeId)
		{
			ActionCard card = new ActionCard { TypeId = typeId ?? string.Empty };
			switch (card.TypeId)
			{
				case "Hit":
					card.Fields["Target"] = "%TARGET";
					break;
				case "AddDamage":
					card.Fields["Type"] = "DAMAGE_PHYSICAL";
					card.Fields["Damage"] = "stat(%CASTER, #baseDamage)";
					break;
				case "ApplyModifier":
				case "RemoveModifier":
				case "AttachEffect":
				case "Heal":
				case "Knockback":
				case "SetStat":
				case "GiveArmor":
				case "KillUnit":
				case "MoveUnit":
					card.Fields["Target"] = "%TARGET";
					break;
				case "TrackingProjectile":
					card.Fields["Target"] = "%TARGET";
					card.Fields["Model"] = "SimpleArrowProjectile";
					break;
				case "PlaySound":
				case "StopSound":
					card.Fields["Unit"] = "%CASTER";
					break;
				case "SpawnUnit":
					card.Fields["Position"] = "unitPosition(%TARGET)";
					break;
				case "Delay":
					card.Fields["Time"] = "0.3";
					break;
				case "Times":
					card.Fields["Times"] = "2";
					break;
				case "Lua":
					card.Fields["Action"] = AbilityLuaRules.DefaultAction;
					break;
			}

			return card;
		}
	}
}
