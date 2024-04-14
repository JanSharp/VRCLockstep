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
  class_name = "LockStepImportedGS",
  fields = {
    {type = "string", name = "internalName"},
    {type = "string", name = "displayName"},
    {type = "uint", name = "dataVersion"},
    {type = "byte[]", name = "binaryData"},
    {type = "LockStepGameState", name = "gameState"},
    {type = "int", name = "gameStateIndex"},
    {type = "string", name = "errorMsg"},
  },
}
