PROJECT EVE — SOURCE CLEANUP 1

REPLACE:
  Characters/Base/CharacterFactory.cs
  AI/LineBankService.cs

IMPORTANT:
1) CharacterFactory.cs had an accidental file-scoped namespace:
     namespace ProjectEve.AI.Brain;
   immediately before:
     namespace ProjectEve.Characters.Characters
   C# forbids mixing those namespace forms in one file. The fixed file removes
   the accidental Brain namespace and keeps the intended CharacterFactory namespace.

2) The uploaded AI/LineBankService.cs itself contains one LineBankService class.
   If Visual Studio still reports duplicate LineBankService / LineHit / ComboHit
   after replacing it, there is ANOTHER .cs file in the project declaring the same
   ProjectEve.AI types. Search the whole solution for:
     class LineBankService
     record LineHit
     record ComboHit
   Keep ONLY AI/LineBankService.cs as an active .cs source file.
   Rename any old copy to .cs.disabled/.txt or remove it from the project.

3) Brain.cs is still needed to clean the remaining duplicate Brain/namespace issue.
   Please upload the CURRENT AI/Brain.cs file after this rebuild if Brain errors remain.
