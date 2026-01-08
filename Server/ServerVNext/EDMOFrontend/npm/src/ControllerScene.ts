import {EdmoModel} from "./EdmoModel";
import {OscillatorState} from "./EdmoProperty";

import {
    ArcRotateCamera,
    Engine,
    HemisphericLight,
    Vector3,
    Color3,
    Color4,
    FramingBehavior,
    Scene,
    SceneOptions
} from "@babylonjs/core";

export class ControllerScene extends Scene {
    private readonly edmoModel: EdmoModel = null!;
    private readonly camera: ArcRotateCamera;
    private readonly engine: Engine;
    private readonly canvas: HTMLCanvasElement;

    private readonly resizeObserver: ResizeObserver;

    private loaded = false;

    public constructor(canvas: HTMLCanvasElement, options?: SceneOptions | undefined) {
        const engine = new Engine(canvas);
        super(engine, options);

        this.engine = engine;
        this.canvas = canvas;

        const light = new HemisphericLight("Global lights", new Vector3(0, 1, 0), this);
        light.diffuse = new Color3(1, 1, 1);
        light.specular = new Color3(1, 1, 1);
        light.groundColor = new Color3(0.4, 0.4, 0.4);


        this.edmoModel = new EdmoModel(this);
        this.clearColor = new Color4(0, 0, 0, 0);

        let camera = this.camera = new ArcRotateCamera("Camera2", 0.4, 0.9, 5, Vector3.ZeroReadOnly, this);
        camera.attachControl(canvas, true);

        camera.useFramingBehavior = true;
        if (camera.framingBehavior)
            camera.framingBehavior.mode = FramingBehavior.FitFrustumSidesMode;

        //window.addEventListener("resize", () => this.Resize());

        this.resizeObserver = new ResizeObserver(_ => this.Resize());

        engine.runRenderLoop(() => {
            this.edmoModel.Update();
            this.render();
        });
    }

    public SignalDisposal() {
        this.engine.dispose()
    }

    public async loadAsync() {
        await this.edmoModel.loadAsync();

        this.camera.setTarget(this.edmoModel.boundingSphere);
        this.resizeObserver.observe(this.canvas);

        this.loaded = true;
    }

    public setColour(hue: number) {
        this.edmoModel.setColour(hue);
    }

    public Resize() {
        if (!this.loaded)
            return;
        this.engine.resize();
        this.camera.setTarget(this.edmoModel.boundingSphere);
    }

    public SynchroniseState(state: OscillatorState) {
        this.edmoModel.synchroniseState(state);
    }
}
