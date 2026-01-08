# Introduction

ServerCore is the core library used by EDMO research studies. It provides a fundamental set of features that can be mixed and matched to create your own control interface. 

All functionality implemented outside of the `EDMO` namespace is designed to be agnostic. Most can be derived or instantiated with different properties to suit different communication protocols or use cases. 

To keep things simple and approachable, not all implementation details are exposed. If you need more direct control of the underlying type, one can provide their own implementation that implements the interfaces provided. Feel free to make an issue or pull request if you believe that certain functionality should be provided that benefits a majority of use cases.

ServerCore also tries not to rely on external libraries, avoiding potential dependency chain issues.
