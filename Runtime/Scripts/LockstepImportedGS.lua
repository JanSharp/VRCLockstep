#! /usr/bin/env vrc_class_gen.lua

return {
  is_class_definition = true,
  usings = {
    "UdonSharp",
    "UnityEngine",
    "VRC.SDKBase",
    "VRC.Udon",
  },
  namespace = "JanSharp",
  class_name = "LockstepImportedGS",
  fields = {
    {type = "string", name = "internalName"},
    {type = "string", name = "displayName"},
    {type = "uint", name = "dataVersion"},
    {type = "byte[]", name = "binaryData"},
    {type = "LockstepGameState", name = "gameState"},
    {type = "int", name = "gameStateIndex"},
    {type = "string", name = "errorMsg"},
    {type = "LockstepGameStateOptionsData", name = "importOptions"},
  },
}
