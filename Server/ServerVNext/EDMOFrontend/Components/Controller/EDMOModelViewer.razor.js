import {ControllerScene} from "../../npm/src/ControllerScene"

export class EDMOModelViewer {

    static async setColour(hue) {
        this.EDMOCanvasScene.setColour(hue)
    }


    static async loadCanvas() {
        const canvas = document.getElementById("EDMOCanvas");
        this.EDMOCanvasScene = new ControllerScene(canvas);
        await this.EDMOCanvasScene.loadAsync();
    }

    static disposeCanvas() {
        if (this.EDMOCanvasScene == null)
            return;
        this.EDMOCanvasScene.SignalDisposal()
        this.EDMOCanvasScene = null;
    }

    static synchroniseState(state) {
        if (this.EDMOCanvasScene == null)
            return;

        this.EDMOCanvasScene.SynchroniseState(state);
    }

}

window.EDMOModelViewer = EDMOModelViewer