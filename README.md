# Animatedtextileforms
All the digital tools, scripts, and code that made the graduation thesis Animated Textileforms Towards Architectural Scale possible

Contents
1. FABRIK script

The core FABRIK (Forward And Backward Reaching Inverse Kinematics) script used throughout the thesis to make arc/catenoid geometry mathematically navigable.

2. Parameterised design space (base version)

The simplest Grasshopper file demonstrating how the FABRIK script functions as a parameterised design space: the minimal setup, useful as a starting point for understanding how the parameters connect.

3. Demonstrator version

The expanded version of the same Grasshopper definition, adapted for use in the final case study demonstrator.


4. AdaCAD headless pipeline

A Node.js script (run_ada.cjs) for generating large-scale weaving drafts from image inputs outside of AdaCAD's own interface, built to get around memory limitations when materializing very large draft graphs at full resolution.

5. Arduino code

The microcontroller code used to drive the SMA actuation. With light and temperature sensor as input and mosfet as output.
