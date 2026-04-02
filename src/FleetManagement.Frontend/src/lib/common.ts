
function extractLicensePlate(message: string): string {
    // Extract license plate from message like "Low fuel for DO-FL-0014 at level 18.5%"
    const match = message.match(/for\s+([A-Z0-9-]+)\s+at/);
    return match ? match[1] : '';
}

export interface AlertMessage {
    start: string;
    licensePlate: string;
    end: string;
}


export function splitMessage(message: string): AlertMessage {
    const m = message.match(/^(.*?\bfor\s+)([A-Z0-9-]+)(\s+at\b.*)$/i);
    if (!m) return {start: message, licensePlate: "", end: ""};
    return {start: m[1], licensePlate: m[2], end: m[3]};
}