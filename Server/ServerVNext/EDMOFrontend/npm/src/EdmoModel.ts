import {
    AbstractMesh,
    ImportMeshAsync,
    Scene,
    Color3,
    CreateSphere,
    StandardMaterial,
    TransformNode, PBRMaterial
} from "@babylonjs/core";
import {registerBuiltInLoaders} from "@babylonjs/loaders/dynamic";
import {OscillatorState} from "./EdmoProperty";
import {isNumeric} from "@babylonjs/core/FlowGraph/utils";


export class EdmoModel {
    private static readonly DEG2RADFACTOR = Math.PI / 180;
    public boundingSphere: AbstractMesh = null!;
    private armModel: TransformNode | null = null!;
    private scene: Scene;

    public constructor(scene: Scene) {
        this.scene = scene;
    }

    public async loadAsync() {
        registerBuiltInLoaders()
        await ImportMeshAsync("arm.glb", this.scene)
        this.boundingSphere = CreateSphere("t", {diameter: 8});
        this.boundingSphere.setEnabled(false);

        const armModel = this.armModel = this.scene.getNodeByName("Arm") as TransformNode | null;
        if (armModel == null)
            return;
        armModel.rotationQuaternion = null;
    }

    public setColour(hue: number) {
        const mat = this.scene.getMaterialByName("Paint");

        console.log(mat)
        console.log(hue)
        console.log()

        if (mat == null || hue == undefined)
            return;

        (mat as PBRMaterial).albedoColor = Color3.FromHSV(hue, 1, 0.5)
    }

    private state: OscillatorState = new class implements OscillatorState {
        amplitude: number = 0;
        frequency: number = 0;
        offset: number = 0;
        phase: number = 0;
        phaseShift: number = 0;
    }

    public synchroniseState(state: OscillatorState) {
        this.state = state;
    }

    public Update() {
        const dt = this.scene.deltaTime / 1000;

        // Don't update shit if dt is 0
        if (!isNumeric(dt, false))
            return;
        
        // Apply derivative from last tick/frame/timestep
        this.state.phase += (Math.PI * 2 * this.state.frequency * dt)
        let position = this.state.amplitude * Math.sin(this.state.phase - this.state.phaseShift) + this.state.offset;

        position = Math.min(Math.max(25, position), 180);

        if (this.armModel == null)
            return;

        this.armModel.rotation.z = -(position - 90) * EdmoModel.DEG2RADFACTOR;
    }
}