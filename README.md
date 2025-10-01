hl2 mounter

currently implemented:

models (including ragdolls and props with proper joints/collisions), materials, textures, sounds

TODO:

flexes/morphs once accessible via the API
animations (along with a SENT for the sandbox gamemode to play individual animations if needed)
maps/scenes once also accessible/have a valid example from an official mount

compile instructions:
dotnet build .\hl2.csproj -c Release in proj folder, find the Sandbox.Mounting.hl2.dll and put it in sbox game folder, mount\hl2\ - afterwards copy Assets\shaders\ into there from this folder
