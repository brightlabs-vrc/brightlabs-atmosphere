# Atmosphere System for Bright Labs Worlds

The following code here are the same code we use internally at Bright Labs for Nakayama Racecourse and every world published by Bright Labs. While we took considerable effort to make this system approachable for our designers, it is left as an exercise for external contributors on how to adapt this to their own worlds.

## What's on this repo?

This repo contains the following:

- The shader setup we use for Nakayama Racecourse's Weather Effects 
  - Nakayama uses a sphere screen-space overlay to deliver dynamic weather effects ensuring complex effects are drawn always as one quad for Quest compatibility.

- The shader setup for the day/night cycle and cloud effects
  - Our skybox assumes a certain setup, by having one directional realtime light with shadows off (for obvious reasons). It also requires the Atmosphere Controller as a component in VRCWorld to drive the weather effects.

- The actual Atmosphere Controller Code
  - This requires it to be a component in the base `VRCWorld` game object, and uses OpenMeteo API for real-time weather.
  - Our controller setup assumes a virtual environment based on a real life location. If you do not want this, you will have to make your own controller based on this code and tailor it for your own.

We do not include our lightmap setup since it requires a very specific setup and demands a graphics card that has hardware raytracing capabilities.