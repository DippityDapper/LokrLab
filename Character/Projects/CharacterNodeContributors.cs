using System;
using System.Collections.Generic;
using LokrLab.Editor;
using LokrLab.Editor.General;
using LokrLabApi;
using LokrCharacterLab;
using LokrLab;

namespace LokrLab.Projects
{
	/// <summary>Character project type's Node Tree contributors and Add Node factories.</summary>
	internal static class CharacterNodeContributors
	{
		/// <summary>The open character as a single top-level node.</summary>
		internal static IEnumerable<LabNode> ContributeCharacter(ProjectSession session)
		{
			if (session == null)
			{
				yield break;
			}

			LabNode character = new LabNode
			{
				Id = "character:" + session.Id,
				DisplayName = session.DisplayName,
				Kind = CharacterNodeKinds.Character,
				IconKey = "Char",
				Payload = session
			};

			foreach (PropertiesCategoryRegistry.CategoryEntry category in PropertiesCategoryRegistry.Categories)
			{
				character.Children.Add(new LabNode
				{
					Id = "props:" + category.Name,
					DisplayName = category.DisplayLabel,
					Kind = CharacterNodeKinds.PropertiesCategory,
					IconKey = "Prop",
					Payload = category.Name
				});
			}

			yield return character;
		}

		/// <summary>A Rig node whose children are parts from rig.json — the SceneTreePanel port.</summary>
		internal static IEnumerable<LabNode> ContributeRig(ProjectSession session)
		{
			if (session == null || string.IsNullOrEmpty(session.FolderPath))
			{
				yield break;
			}

			LabNode rig = new LabNode
			{
				Id = "rig:" + session.Id,
				DisplayName = "Rig",
				Kind = CharacterNodeKinds.Rig,
				IconKey = "Rig",
				Payload = session.FolderPath
			};

			List<string> parts = CharacterRigOutline.ReadPartNames(session.FolderPath);
			for (int i = 0; i < parts.Count; i++)
			{
				string name = parts[i];
				rig.Children.Add(new LabNode
				{
					Id = "part:" + name,
					DisplayName = name,
					Kind = CharacterNodeKinds.Part,
					IconKey = "Part",
					Payload = name
				});
			}

			yield return rig;
		}

		/// <summary>An Animator node whose children are clip names from rig.json.</summary>
		internal static IEnumerable<LabNode> ContributeAnimator(ProjectSession session)
		{
			if (session == null || string.IsNullOrEmpty(session.FolderPath))
			{
				yield break;
			}

			LabNode animator = new LabNode
			{
				Id = "animator:" + session.Id,
				DisplayName = "Animator",
				Kind = CharacterNodeKinds.Animator,
				IconKey = "Anim",
				Payload = session.FolderPath
			};

			List<string> clips = CharacterRigOutline.ReadAnimationNames(session.FolderPath);
			for (int i = 0; i < clips.Count; i++)
			{
				string name = clips[i];
				animator.Children.Add(new LabNode
				{
					Id = "clip:" + name,
					DisplayName = name,
					Kind = CharacterNodeKinds.AnimationClip,
					IconKey = "Clip",
					Payload = name
				});
			}

			yield return animator;
		}

		/// <summary>Cross-project ability ids this character references (skills, defaultSkill, skillProgression).</summary>
		internal static IEnumerable<LabNode> ContributeAbilities(ProjectSession session)
		{
			if (session == null)
			{
				yield break;
			}

			LabNode folder = new LabNode
			{
				Id = "abilities:" + session.Id,
				DisplayName = "Abilities",
				Kind = CharacterNodeKinds.Abilities,
				IconKey = "Abil",
				Payload = session
			};

			foreach (string skillId in CollectReferencedAbilityIds())
			{
				folder.Children.Add(new LabNode
				{
					Id = "abilityref:" + skillId,
					DisplayName = skillId,
					Kind = CharacterNodeKinds.AbilityRef,
					IconKey = "Abil",
					Payload = skillId
				});
			}

			yield return folder;
		}

		/// <summary>This character folder's aliases.json, sibling of Abilities.</summary>
		internal static IEnumerable<LabNode> ContributeAliases(ProjectSession session)
		{
			if (session == null || string.IsNullOrEmpty(session.FolderPath))
			{
				yield break;
			}

			yield return new LabNode
			{
				Id = "aliases:" + session.Id,
				DisplayName = "Aliases",
				Kind = CharacterNodeKinds.Aliases,
				IconKey = "Abil",
				Payload = session.FolderPath
			};
		}

		/// <summary>Opens the Ability Library with this reference pre-selected, when that project type is registered.</summary>
		internal static void JumpToAbility(string abilityId)
		{
			if (string.IsNullOrEmpty(abilityId))
			{
				return;
			}

			if (LokrLabApi.LokrLabApi.GetProjectType(LokrLabApi.LokrLabApi.AbilityLibraryTypeId) == null)
			{
				Lab.SetStatus("Ability Library is not installed.");
				return;
			}

			LokrLabApi.LokrLabApi.JumpToProject(
				LokrLabApi.LokrLabApi.AbilityLibraryTypeId,
				CharacterLabPaths.FindAbilityLibraryFolder(abilityId),
				"ability:" + abilityId);
		}

		private static List<string> CollectReferencedAbilityIds()
		{
			List<string> ids = new List<string>();
			HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			CharacterProfile profile = CharacterSession.Profile;
			if (profile == null)
			{
				return ids;
			}

			AddAbilityId(ids, seen, profile.DefaultSkill);
			if (profile.Skills != null)
			{
				foreach (string skillId in profile.Skills)
				{
					AddAbilityId(ids, seen, skillId);
				}
			}

			if (profile.SkillProgression != null)
			{
				foreach (LevelSkillEntry entry in profile.SkillProgression)
				{
					if (entry == null || entry.SkillIds == null)
					{
						continue;
					}

					foreach (string skillId in entry.SkillIds)
					{
						AddAbilityId(ids, seen, skillId);
					}
				}
			}

			ids.Sort(StringComparer.OrdinalIgnoreCase);
			return ids;
		}

		private static void AddAbilityId(List<string> ids, HashSet<string> seen, string skillId)
		{
			if (string.IsNullOrEmpty(skillId) || !seen.Add(skillId))
			{
				return;
			}

			ids.Add(skillId);
		}

		/// <summary>Adds a uniquely named part under Rig when the rig has no authored frames.</summary>
		internal static LabNode CreatePart(LabNode parent, ProjectSession session)
		{
			if (session == null || string.IsNullOrEmpty(session.FolderPath))
			{
				return null;
			}

			string name = NextUniqueName(CharacterRigOutline.ReadPartNames(session.FolderPath), "Part");
			if (!CharacterRigOutline.TryAddPart(session.FolderPath, name, out string error))
			{
				LokrCharacterLabPlugin.Log.LogWarning("CreatePart: " + error);
				Lab.SetStatus(error);
				return null;
			}

			Lab.SetStatus("Added part '" + name + "'.");
			return new LabNode
			{
				Id = "part:" + name,
				DisplayName = name,
				Kind = CharacterNodeKinds.Part,
				IconKey = "Part",
				Payload = name
			};
		}

		/// <summary>Adds a uniquely named empty clip under Animator when the rig has no authored frames.</summary>
		internal static LabNode CreateAnimationClip(LabNode parent, ProjectSession session)
		{
			if (session == null || string.IsNullOrEmpty(session.FolderPath))
			{
				return null;
			}

			string name = NextUniqueName(CharacterRigOutline.ReadAnimationNames(session.FolderPath), "NewClip");
			if (!CharacterRigOutline.TryAddAnimation(session.FolderPath, name, out string error))
			{
				LokrCharacterLabPlugin.Log.LogWarning("CreateAnimationClip: " + error);
				Lab.SetStatus(error);
				return null;
			}

			Lab.SetStatus("Added clip '" + name + "'.");
			return new LabNode
			{
				Id = "clip:" + name,
				DisplayName = name,
				Kind = CharacterNodeKinds.AnimationClip,
				IconKey = "Clip",
				Payload = name
			};
		}

		private static string NextUniqueName(List<string> existing, string prefix)
		{
			int n = 1;
			string candidate = prefix;
			while (ContainsName(existing, candidate))
			{
				n++;
				candidate = prefix + n;
			}

			return candidate;
		}

		private static bool ContainsName(List<string> existing, string name)
		{
			for (int i = 0; i < existing.Count; i++)
			{
				if (string.Equals(existing[i], name, System.StringComparison.OrdinalIgnoreCase))
				{
					return true;
				}
			}

			return false;
		}
	}
}
